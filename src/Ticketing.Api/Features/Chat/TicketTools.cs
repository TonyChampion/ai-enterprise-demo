using System.ComponentModel;
using System.Diagnostics;
using System.Security.Claims;
using Ticketing.Api.Data;
using Ticketing.Api.Domain;
using Ticketing.Api.Infrastructure;

namespace Ticketing.Api.Features.Chat;

public record TicketSummary(int Number, string Customer, string Title, TicketStatus Status, TicketPriority Priority);

/// <summary>
/// The tools chat can use. Identity comes from the authenticated request, never from the model.
/// A fully jailbroken model can still only see what this user could see in the normal UI.
/// </summary>
public class TicketTools(ITicketStore store, ClaimsPrincipal user)
{
    [Description("Get the signed-in user's open tickets")]
    public async Task<IReadOnlyList<TicketSummary>> GetMyOpenTickets()
    {
        var customer = user.CustomerId();          // from the token, never from the model
        var tickets = await store.ListAsync();
        var mine = tickets
            .Where(t => user.IsAgent() || t.CustomerId == customer)   // the filter lives in code, not the prompt
            .Where(IsOpen)
            .Select(ToSummary)
            .ToList();
        ToolAudit.Record(scope: user.IsAgent() ? "agent:all" : customer, mine);
        return mine;
    }

    [Description("Get one ticket by its number, if the signed-in user is allowed to see it")]
    public async Task<TicketSummary?> GetTicket(int number)
    {
        var ticket = await store.GetByNumberAsync(number);
        // A foreign ticket number gets the same answer as a missing one
        var allowed = ticket is not null && (user.IsAgent() || ticket.CustomerId == user.CustomerId());
        var result = allowed ? ToSummary(ticket!) : null;
        ToolAudit.Record(scope: user.CustomerId(), result is null ? [] : [result]);
        return result;
    }

    internal static bool IsOpen(Ticket t) => t.Status is TicketStatus.Open or TicketStatus.InProgress;
    internal static TicketSummary ToSummary(Ticket t) => new(t.Number, t.CustomerId, t.Title, t.Status, t.Priority);
}

/// <summary>
/// The confused deputy. The app can read every customer's tickets, and the model decides which
/// customer to ask for. Which means the user decides. Demo 3, beat 3. Never ship this.
/// </summary>
public class NaiveTicketTools(ITicketStore store)
{
    [Description("Get open tickets for a customer")]
    public async Task<IReadOnlyList<TicketSummary>> GetOpenTickets(string customerId)
    {
        var tickets = await store.ListAsync();
        var result = tickets
            .Where(t => t.CustomerId.Equals(customerId, StringComparison.OrdinalIgnoreCase))   // the model picks the customer
            .Where(TicketTools.IsOpen)
            .Select(TicketTools.ToSummary)
            .ToList();
        ToolAudit.Record(scope: $"model-chosen:{customerId}", result);
        return result;
    }

    [Description("Get one ticket by its number")]
    public async Task<TicketSummary?> GetTicket(int number)
    {
        var ticket = await store.GetByNumberAsync(number);
        var result = ticket is null ? null : TicketTools.ToSummary(ticket);
        ToolAudit.Record(scope: "model-chosen", result is null ? [] : [result]);
        return result;
    }
}

/// <summary>
/// What the tool was allowed to return, on the execute_tool span. IDs and counts, not ticket text:
/// enough to audit what the AI actually did, not what it said it did.
/// </summary>
internal static class ToolAudit
{
    public static void Record(string scope, IReadOnlyCollection<TicketSummary> returned)
    {
        var span = Activity.Current;
        if (span is null) return;
        span.SetTag("tool.scope", scope);
        span.SetTag("tool.result.count", returned.Count);
        span.SetTag("tool.result.customers", string.Join(",", returned.Select(t => t.Customer).Distinct()));
        span.SetTag("tool.result.tickets", string.Join(",", returned.Select(t => t.Number)));
    }
}
