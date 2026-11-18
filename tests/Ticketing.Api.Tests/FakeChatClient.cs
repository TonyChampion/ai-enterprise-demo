using Microsoft.Extensions.AI;

namespace Ticketing.Api.Tests;

/// <summary>A model that says whatever the test tells it to. Including things a fooled model would say.</summary>
public sealed class FakeChatClient(Func<IEnumerable<ChatMessage>, string> reply) : IChatClient
{
    public static FakeChatClient Returning(string json) => new(_ => json);
    public static FakeChatClient Throwing(Exception ex) => new(_ => throw ex);

    public IList<ChatMessage> LastMessages { get; private set; } = [];

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        LastMessages = messages.ToList();
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply(LastMessages))));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
}
