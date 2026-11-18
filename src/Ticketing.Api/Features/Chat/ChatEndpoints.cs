using System.Diagnostics;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.AI;
using Ticketing.Api.Ai;
using Ticketing.Api.Data;
using Ticketing.Api.Infrastructure;

namespace Ticketing.Api.Features.Chat;

public record ChatTurn(string Role, string Text);
public record ChatRequest(List<ChatTurn> History);
public record ChatChunk(string? Text = null, string? Error = null, string? Kind = null, long? ElapsedMs = null);
public record ChatAnswer(string Text, long ElapsedMs);

public static class ChatEndpoints
{
    public static RouteGroupBuilder MapChat(this RouteGroupBuilder group)
    {
        // Streamed: Server-Sent Events, built into .NET 10
        group.MapPost("/", (ChatRequest req, [FromKeyedServices("chat")] IChatClient chat, ClaimsPrincipal user,
                ITicketStore store, DemoSwitches switches, ILoggerFactory logs, CancellationToken ct) =>
            TypedResults.ServerSentEvents(
                StreamChat(req, chat, Build(req, user, store, switches), logs.CreateLogger("Chat"), ct)))
            .WithName("Chat");

        // Buffered: the same question, nothing on screen until the whole answer exists
        group.MapPost("/buffered", Buffered).WithName("ChatBuffered");
        return group;
    }

    private static (List<ChatMessage> Messages, ChatOptions Options) Build(
        ChatRequest req, ClaimsPrincipal user, ITicketStore store, DemoSwitches switches)
    {
        var options = new ChatOptions
        {
            Tools = switches.ChatTools == ChatToolsMode.Safe
                ? [AIFunctionFactory.Create(new TicketTools(store, user).GetMyOpenTickets),
                   AIFunctionFactory.Create(new TicketTools(store, user).GetTicket)]
                : [AIFunctionFactory.Create(new NaiveTicketTools(store).GetOpenTickets),
                   AIFunctionFactory.Create(new NaiveTicketTools(store).GetTicket)]
        };

        // "Only discuss tickets for the current customer" is a suggestion, not a security boundary.
        // Assume this prompt will leak. Nothing in it is a secret or an access rule we rely on.
        var system = $"""
            You are the support assistant for a ticketing portal.
            The signed-in user is {user.UserId()} from customer "{user.CustomerId()}".
            Only discuss tickets for the current customer. Use the tools to look tickets up.
            Answer in a few short sentences or a short list. Always include ticket numbers.
            """;

        var messages = new List<ChatMessage> { new(ChatRole.System, system) };
        messages.AddRange(req.History.TakeLast(12).Select(t =>
            new ChatMessage(t.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? ChatRole.Assistant : ChatRole.User, t.Text)));
        return (messages, options);
    }

    private static async IAsyncEnumerable<SseItem<ChatChunk>> StreamChat(
        ChatRequest req, IChatClient chat, (List<ChatMessage> Messages, ChatOptions Options) call, ILogger logger,
        [EnumeratorCancellation] CancellationToken ct)   // the request-aborted token
    {
        var clock = Stopwatch.StartNew();
        long? firstTokenMs = null;
        var chars = 0;

        await using var updates = chat.GetStreamingResponseAsync(call.Messages, call.Options, ct).GetAsyncEnumerator(ct);
        while (true)
        {
            ChatResponseUpdate? update = null;
            AiFailure? failure = null;
            try
            {
                if (!await updates.MoveNextAsync()) break;
                update = updates.Current;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Tab closed. The model call is cancelled with the request: no more tokens billed for nobody.
                logger.LogWarning("Client went away after {Chars} chars and {Ms} ms. Model call cancelled.", chars, clock.ElapsedMilliseconds);
                Activity.Current?.SetTag("chat.cancelled_by_client", true);
                yield break;
            }
            catch (Exception ex)
            {
                failure = AiFailure.From(ex);
            }

            if (failure is not null)
            {
                logger.LogWarning("Chat failed: {Kind} ({Status}) {Detail}", failure.Kind, failure.Status, failure.Detail);
                // We already returned a 200. In a stream, failure is an event, not a status code.
                yield return new SseItem<ChatChunk>(new ChatChunk(Error: Describe(failure), Kind: failure.Kind.ToString()), "error");
                yield break;
            }

            if (!string.IsNullOrEmpty(update!.Text))
            {
                firstTokenMs ??= clock.ElapsedMilliseconds;
                chars += update.Text.Length;
                yield return new SseItem<ChatChunk>(new ChatChunk(Text: update.Text, ElapsedMs: clock.ElapsedMilliseconds), "delta");
            }
        }

        logger.LogInformation("Chat streamed {Chars} chars. First words at {First} ms, done at {Total} ms.", chars, firstTokenMs, clock.ElapsedMilliseconds);
        yield return new SseItem<ChatChunk>(new ChatChunk(ElapsedMs: clock.ElapsedMilliseconds), "done");
    }

    private static async Task<Results<Ok<ChatAnswer>, ProblemHttpResult>> Buffered(
        ChatRequest req, [FromKeyedServices("chat")] IChatClient chat, ClaimsPrincipal user,
        ITicketStore store, DemoSwitches switches, CancellationToken ct)
    {
        var (messages, options) = Build(req, user, store, switches);
        var clock = Stopwatch.StartNew();
        try
        {
            var response = await chat.GetResponseAsync(messages, options, ct);
            return TypedResults.Ok(new ChatAnswer(response.Text, clock.ElapsedMilliseconds));
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            var failure = AiFailure.From(ex);
            return TypedResults.Problem(
                statusCode: failure.Kind == AiFailureKind.RateLimited ? 429 : 502,
                title: failure.Kind.ToString(), detail: Describe(failure));
        }
    }

    private static string Describe(AiFailure f) => f.Kind switch
    {
        AiFailureKind.RateLimited => "Chat is busy right now (token limit). Try again in a moment.",
        AiFailureKind.SafetyBlocked => "That message was blocked by content safety.",
        AiFailureKind.ModelFiltered => "The model's content filter refused that request.",
        AiFailureKind.Denied => $"Model access denied ({f.Status}). {f.Detail}",
        AiFailureKind.Timeout => "The model took too long. Try again.",
        _ => $"Chat is unavailable: {f.Detail}"
    };
}
