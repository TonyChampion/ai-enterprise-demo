namespace Ticketing.Api.Domain;

public enum TicketStatus { Open, InProgress, Resolved, Closed }

public enum TicketPriority { Low, Normal, High, Urgent }

/// <summary>What triage can choose. "Resolved" means no action needed: the naive pipeline auto-resolves on it.</summary>
public enum TicketCategory { Login, Billing, Bug, Performance, FeatureRequest, Account, Resolved, Other }

public class Ticket
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>The number people use: "ticket 4471".</summary>
    public int Number { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public TicketStatus Status { get; set; } = TicketStatus.Open;
    public TicketPriority Priority { get; set; } = TicketPriority.Normal;
    /// <summary>The customer (tenant) the ticket belongs to: contoso, fabrikam, ...</summary>
    public string CustomerId { get; set; } = "";
    public string RequesterId { get; set; } = "";
    public string? AssigneeId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<Comment> Comments { get; set; } = [];
    public TriageInfo Triage { get; set; } = new();

    // What the customer submitted, so re-running triage always starts from the same place
    internal TicketStatus? SubmittedStatus { get; set; }
    internal TicketPriority? SubmittedPriority { get; set; }

    /// <summary>Undo whatever an earlier triage run did. Demo 2 re-runs ticket 4471 several times.</summary>
    public void ResetToSubmitted()
    {
        SubmittedStatus ??= Status;
        SubmittedPriority ??= Priority;
        Status = SubmittedStatus.Value;
        Priority = SubmittedPriority.Value;
    }
}

/// <summary>What triage produced and what the app did with it.</summary>
public class TriageInfo
{
    public TriageState State { get; set; } = TriageState.NotRun;
    public TicketCategory? Category { get; set; }
    public string? Queue { get; set; }
    public string? Summary { get; set; }
    public double? Confidence { get; set; }
    /// <summary>Why it needs a person, or what was rejected. Shown in the UI.</summary>
    public string? Note { get; set; }
    public string? Route { get; set; }
    public string? PromptMode { get; set; }
    public DateTimeOffset? At { get; set; }
}

public enum TriageState { NotRun, Applied, NeedsHuman, Pending, Failed }

public class Comment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TicketId { get; set; }
    public string AuthorId { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
