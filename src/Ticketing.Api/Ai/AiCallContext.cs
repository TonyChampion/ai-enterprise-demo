using System.Diagnostics;
using Microsoft.Extensions.AI;

namespace Ticketing.Api.Ai;

/// <summary>
/// Business context for the AI call in flight: which ticket, which caller.
/// IDs that join back to our data, never the data itself.
/// </summary>
public static class AiCallContext
{
    private static readonly AsyncLocal<int?> CurrentTicket = new();

    public static int? TicketNumber => CurrentTicket.Value;

    public static IDisposable ForTicket(int number)
    {
        var previous = CurrentTicket.Value;
        CurrentTicket.Value = number;
        Activity.Current?.SetTag("ticket.id", number);   // on the request span too
        return new Restore(() => CurrentTicket.Value = previous);
    }

    private sealed class Restore(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }
}

/// <summary>
/// Sits inside UseOpenTelemetry, so Activity.Current is the gen_ai "chat" span.
/// Adds our own tags to it: caller, ticket.id, and the route the call took.
/// Tags on the parent request span do not copy themselves onto child spans.
/// </summary>
public sealed class TaggingChatClient(IChatClient inner, string caller, DemoSwitches switches) : DelegatingChatClient(inner)
{
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Tag();
        return base.GetResponseAsync(messages, options, cancellationToken);
    }

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Tag();
        return base.GetStreamingResponseAsync(messages, options, cancellationToken);
    }

    private void Tag()
    {
        var span = Activity.Current;
        if (span is null) return;
        span.SetTag("caller", caller);
        span.SetTag("ai.route", switches.Route.ToString());
        if (AiCallContext.TicketNumber is { } number)
            span.SetTag("ticket.id", number);
    }
}
