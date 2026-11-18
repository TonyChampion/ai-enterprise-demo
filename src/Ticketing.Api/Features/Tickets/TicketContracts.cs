using System.ComponentModel.DataAnnotations;
using Ticketing.Api.Domain;

namespace Ticketing.Api.Features.Tickets;

// What a caller may send. No Id, Status, or CustomerId: the server owns those (customer comes from the token).
public record CreateTicketRequest(
    [Required, StringLength(200)] string Title,
    [StringLength(4000)] string Description,
    TicketPriority Priority);

public record TicketResponse(
    Guid Id,
    int Number,
    string Title,
    string Description,
    TicketStatus Status,
    TicketPriority Priority,
    string CustomerId,
    string RequesterId,
    string? AssigneeId,
    TriageInfo Triage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public static class TicketMappings
{
    public static TicketResponse ToResponse(this Ticket t) => new(
        t.Id, t.Number, t.Title, t.Description, t.Status, t.Priority,
        t.CustomerId, t.RequesterId, t.AssigneeId, t.Triage, t.CreatedAt, t.UpdatedAt);
}
