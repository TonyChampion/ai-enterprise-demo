// Demo 1, beat 4: a second app floods the gateway.
//   dotnet run --project tools/RogueCaller            (20 requests, 6 at a time)
//   dotnet run --project tools/RogueCaller -- 40 10   (40 requests, 10 at a time)
//
// Honest caveat for the stage: on your laptop this app and the ticketing API both authenticate as YOU,
// so the gateway's per-caller counter is shared and the flood eats the API's budget too. That is what makes
// the API's 429 handling visible. In Azure each app has its own managed identity, and so its own counter.
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json;
using Azure.AI.OpenAI;
using Azure.Identity;
using OpenAI.Chat;

var total = args.Length > 0 ? int.Parse(args[0]) : 20;
var parallel = args.Length > 1 ? int.Parse(args[1]) : 6;

var settingsPath = Path.Combine(AppContext.BaseDirectory, "../../../../../src/Ticketing.Api/appsettings.Local.json");
using var settings = JsonDocument.Parse(File.ReadAllText(Path.GetFullPath(settingsPath)));
var ai = settings.RootElement.GetProperty("Ai");
var gateway = ai.GetProperty("GatewayEndpoint").GetString()!;
var deployment = ai.GetProperty("ChatDeployment").GetString()!;

var client = new AzureOpenAIClient(new Uri(gateway), new DefaultAzureCredential(),
    new AzureOpenAIClientOptions { RetryPolicy = new ClientRetryPolicy(maxRetries: 0) });
var chat = client.GetChatClient(deployment);

// A big prompt burns the token budget fast
var filler = string.Join(" ", Enumerable.Repeat("Explain the history of distributed systems in detail.", 120));
Console.WriteLine($"Flooding {new Uri(gateway).Host} with {total} requests, {parallel} at a time...");

int ok = 0, limited = 0, other = 0;
await Parallel.ForEachAsync(Enumerable.Range(1, total), new ParallelOptions { MaxDegreeOfParallelism = parallel }, async (i, ct) =>
{
    try
    {
        var result = await chat.CompleteChatAsync([new UserChatMessage(filler)],
            new ChatCompletionOptions { MaxOutputTokenCount = 50 }, ct);
        var remaining = result.GetRawResponse().Headers.TryGetValue("x-remaining-tokens", out var r) ? r : "?";
        Interlocked.Increment(ref ok);
        Console.WriteLine($"  #{i,2}  200  remaining tokens: {remaining}");
    }
    catch (ClientResultException ex) when (ex.Status == 429)
    {
        Interlocked.Increment(ref limited);
        var retry = ex.GetRawResponse()?.Headers.TryGetValue("Retry-After", out var ra) == true ? ra : "?";
        Console.WriteLine($"  #{i,2}  429  over the token limit, retry after {retry}s");
    }
    catch (Exception ex)
    {
        Interlocked.Increment(ref other);
        Console.WriteLine($"  #{i,2}  {(ex as ClientResultException)?.Status.ToString() ?? "ERR"}  {ex.Message.Split('\n')[0]}");
    }
});
Console.WriteLine($"Done: {ok} ok, {limited} rate limited, {other} other.");
