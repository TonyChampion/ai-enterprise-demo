using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Concurrent;
using Azure.AI.OpenAI;
using Azure.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Ticketing.Api.Ai;

/// <summary>
/// Builds the model clients for each route. The code that matters is in Create: the
/// "demo version" and the "AI-ready version" from the slides, side by side.
/// </summary>
public class ModelClients(IOptions<AiOptions> options, TokenCredential credential)
{
    private readonly AiOptions _ai = options.Value;
    private readonly ConcurrentDictionary<(ModelRoute, string), IChatClient> _clients = new();

    public IChatClient For(ModelRoute route, string deployment) =>
        _clients.GetOrAdd((route, deployment), key => Create(key.Item1).GetChatClient(key.Item2).AsIChatClient());

    /// <summary>The host each route talks to. Shown on the demo page.</summary>
    public string HostFor(ModelRoute route) =>
        new Uri(route == ModelRoute.Gateway ? _ai.GatewayEndpoint : _ai.FoundryEndpoint).Host;

    private AzureOpenAIClient Create(ModelRoute route)
    {
        var clientOptions = new AzureOpenAIClientOptions
        {
            // No silent retries on stage: a 429 should show up as a 429 in our handling code.
            // In production keep the SDK's retries (they honor Retry-After) and queue the work.
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
            NetworkTimeout = TimeSpan.FromSeconds(60)
        };

        return route switch
        {
            // The demo version: a key in config, straight to Foundry
            ModelRoute.Key => new AzureOpenAIClient(
                new Uri(_ai.FoundryEndpoint),
                new ApiKeyCredential(string.IsNullOrWhiteSpace(_ai.FoundryKey) ? "no-key-configured" : _ai.FoundryKey),
                clientOptions),

            // Same endpoint, no key: DefaultAzureCredential (your Entra login locally, managed identity in Azure)
            ModelRoute.Identity => new AzureOpenAIClient(
                new Uri(_ai.FoundryEndpoint), credential, clientOptions),

            // The AI-ready version: the gateway, not Foundry. Same credential.
            // The token's audience (cognitiveservices.azure.com) is what the gateway validates.
            ModelRoute.Gateway => new AzureOpenAIClient(
                new Uri(_ai.GatewayEndpoint), credential, clientOptions),

            _ => throw new ArgumentOutOfRangeException(nameof(route))
        };
    }
}

/// <summary>
/// The IChatClient the rest of the app sees. It forwards to whichever route the stage switch says,
/// so triage and chat code never change between demos.
/// </summary>
public sealed class RoutedChatClient(ModelClients clients, DemoSwitches switches, string deployment) : IChatClient
{
    private IChatClient Current => clients.For(switches.Route, deployment);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => Current.GetResponseAsync(messages, options, cancellationToken);

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => Current.GetStreamingResponseAsync(messages, options, cancellationToken);

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceType == typeof(RoutedChatClient) ? this : Current.GetService(serviceType, serviceKey);

    public void Dispose() { }
}
