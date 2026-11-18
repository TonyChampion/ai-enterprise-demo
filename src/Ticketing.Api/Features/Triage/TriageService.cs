using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Ticketing.Api.Ai;
using Ticketing.Api.Data;
using Ticketing.Api.Domain;

namespace Ticketing.Api.Features.Triage;

/// <summary>Triage returns a type, not a paragraph.</summary>
public record TriageResult(
    string Summary, TicketCategory Category, TicketPriority Priority,
    string Queue, double Confidence);

public class TriageService(
    [FromKeyedServices("triage")] IChatClient chat,
    ITicketStore store,
    DemoSwitches switches,
    IOptions<AiOptions> options,
    ILogger<TriageService> logger)
{
    private static readonly JsonSerializerOptions Json = new(AIJsonUtilities.DefaultOptions)
    {
        Converters = { new JsonStringEnumConverter() }   // the schema says "Billing", not 1
    };

    /// <summary>Ask the model. Returns its recommendation, nothing else. The evals call this directly.</summary>
    public async Task<TriageResult> RunAsync(Ticket ticket, PromptMode mode, CancellationToken cancellationToken = default)
    {
        var response = await chat.GetResponseAsync<TriageResult>(
            TriagePrompt.Build(ticket, mode), Json, cancellationToken: cancellationToken);
        return response.Result;
    }

    /// <summary>
    /// Triage a ticket and apply the result. Never throws for AI failures: the ticket is already
    /// saved, and an AI feature should never make the system worse when it fails.
    /// </summary>
    public async Task<Ticket> TriageAsync(Ticket ticket, CancellationToken cancellationToken = default)
    {
        var mode = switches.Prompt;
        ticket.ResetToSubmitted();
        using var _ = AiCallContext.ForTicket(ticket.Number);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TriageTimeoutSeconds));

        var info = new TriageInfo { Route = switches.Route.ToString(), PromptMode = mode.ToString(), At = DateTimeOffset.UtcNow };
        try
        {
            var result = await RunAsync(ticket, mode, timeout.Token);
            if (mode == PromptMode.Naive) ApplyNaive(ticket, result, info);
            else TriageRules.Apply(ticket, result, info);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var failure = AiFailure.From(ex);
            logger.LogWarning("Triage for ticket {Ticket} failed: {Kind} ({Status}) {Detail}",
                ticket.Number, failure.Kind, failure.Status, failure.Detail);

            (info.State, info.Note) = failure.Kind switch
            {
                AiFailureKind.SafetyBlocked => (TriageState.NeedsHuman, $"Blocked by content safety at the gateway ({failure.Status}). Sent to a person."),
                AiFailureKind.ModelFiltered => (TriageState.NeedsHuman, "Refused by the model deployment's content filter. Sent to a person."),
                AiFailureKind.RateLimited => (TriageState.Pending, $"Over the token limit (429). Ticket saved; triage retries {(failure.RetryAfter is { } r ? $"in {r.TotalSeconds:0}s" : "later")}."),
                AiFailureKind.Timeout => (TriageState.Pending, "Model too slow today. Ticket saved; triage retries later."),
                AiFailureKind.Denied => (TriageState.Failed, $"Denied ({failure.Status}): {failure.Detail}"),
                _ => (TriageState.Failed, $"Model unavailable: {failure.Detail}")
            };
        }

        ticket.Triage = info;
        await store.UpdateAsync(ticket, cancellationToken);
        return ticket;
    }

    /// <summary>The naive pipeline trusts the model's output and acts on it.</summary>
    private static void ApplyNaive(Ticket ticket, TriageResult result, TriageInfo info)
    {
        info.State = TriageState.Applied;
        info.Category = result.Category;
        info.Queue = result.Queue;
        info.Summary = result.Summary;
        info.Confidence = result.Confidence;
        ticket.Priority = result.Priority;
        if (result.Category == TicketCategory.Resolved)
            ticket.Status = TicketStatus.Resolved;     // closed without anyone looking at it
    }
}

/// <summary>
/// Schema constrains shape. These rules constrain effects. They still hold when the model is fooled.
/// </summary>
public static class TriageRules
{
    public const double MinConfidence = 0.6;

    public static void Apply(Ticket ticket, TriageResult result, TriageInfo info)
    {
        var problems = new List<string>();

        info.Category = result.Category;
        info.Summary = result.Summary;
        info.Confidence = result.Confidence;

        // Only known queues route work
        info.Queue = TriagePrompt.Queues.Contains(result.Queue, StringComparer.OrdinalIgnoreCase)
            ? TriagePrompt.Queues.First(q => q.Equals(result.Queue, StringComparison.OrdinalIgnoreCase))
            : "General";

        // Triage is a recommendation, not an action: it never resolves a ticket
        if (result.Category == TicketCategory.Resolved)
            problems.Add("Triage suggested Resolved. Triage never closes tickets.");

        // Triage may raise priority, never lower what the customer set
        if (result.Priority > ticket.Priority)
            ticket.Priority = result.Priority;
        else if (result.Priority < ticket.Priority)
            problems.Add($"Triage suggested lowering priority to {result.Priority}. Kept {ticket.Priority}.");

        // A summary never authorizes anything
        if (ContainsApprovalClaim(result.Summary))
            problems.Add("Summary contains an approval or refund claim.");

        if (result.Summary.Contains(TriagePrompt.InjectionFlag, StringComparison.OrdinalIgnoreCase))
            problems.Add("Model flagged instructions aimed at the AI.");

        if (result.Confidence < MinConfidence)
            problems.Add($"Low confidence ({result.Confidence:0.00}).");

        info.State = problems.Count == 0 ? TriageState.Applied : TriageState.NeedsHuman;
        info.Note = problems.Count == 0 ? null : string.Join(" ", problems) + " Sent to a person.";
    }

    private static bool ContainsApprovalClaim(string summary) =>
        summary.Contains("refund", StringComparison.OrdinalIgnoreCase) ||
        summary.Contains("approved", StringComparison.OrdinalIgnoreCase);
}
