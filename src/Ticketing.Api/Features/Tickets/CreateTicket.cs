using FluentValidation;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Ticketing.Api.Data;
using Ticketing.Api.Domain;
using Ticketing.Api.Features.Triage;
using Ticketing.Api.Infrastructure;

namespace Ticketing.Api.Features.Tickets;

public static class CreateTicket
{
    public static RouteGroupBuilder MapCreateTicket(this RouteGroupBuilder group)
    {
        group.MapPost("/", Handle)
            .WithName("CreateTicket")
            .WithValidator<CreateTicketRequest>();
        return group;
    }

    public static async Task<Results<Created<TicketResponse>, ProblemHttpResult>> Handle(
        CreateTicketRequest request, ITicketStore store, TriageService triage, ClaimsPrincipal user, HttpRequest http,
        CancellationToken cancellationToken)
    {
        // The customer comes from the token. Nothing in the request body can choose it.
        var customer = user.CustomerId();
        if (string.IsNullOrEmpty(customer))
            return Problems.Forbidden("Only customer users open tickets.");

        var now = DateTimeOffset.UtcNow;
        var ticket = new Ticket
        {
            Title = request.Title,
            Description = request.Description,
            Priority = request.Priority,
            CustomerId = customer,
            RequesterId = user.UserId(),
            Status = TicketStatus.Open,
            CreatedAt = now,
            UpdatedAt = now
        };

        // Save first. Triage failing must never fail ticket creation.
        await store.AddAsync(ticket, cancellationToken);

        // Inline so the demo shows the result immediately. In production: a queue
        // (the hands-on lab's background module), which also absorbs 429s for free.
        await triage.TriageAsync(ticket, cancellationToken);

        return TypedResults.Created(http.LocationFor(ticket.Id), ticket.ToResponse());
    }
}

public class CreateTicketRequestValidator : AbstractValidator<CreateTicketRequest>
{
    public CreateTicketRequestValidator()
    {
        RuleFor(r => r.Description)
            .MinimumLength(10)
            .When(r => r.Priority == TicketPriority.Urgent)
            .WithMessage("Urgent tickets need a description of at least 10 characters.");
    }
}
