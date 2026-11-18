using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Ticketing.Api.Data;
using Ticketing.Api.Features.Tickets;
using Ticketing.Api.Infrastructure;

namespace Ticketing.Api.Features.Triage;

public static class TriageEndpoints
{
    /// <summary>Run (or re-run) triage on a ticket. Demo 2 runs ticket 4471 as many times as it takes.</summary>
    public static RouteGroupBuilder MapRunTriage(this RouteGroupBuilder group)
    {
        group.MapPost("/{id:guid}/triage", Handle).WithName("RunTriage");
        return group;
    }

    public static async Task<Results<Ok<TicketResponse>, ProblemHttpResult>> Handle(
        Guid id, ITicketStore store, TriageService triage, ClaimsPrincipal user, IAuthorizationService auth,
        CancellationToken cancellationToken)
    {
        var ticket = await store.GetAsync(id, cancellationToken);
        if (ticket is null)
            return Problems.TicketNotFound(id);

        if (!(await auth.AuthorizeAsync(user, ticket, Policies.TicketAccess)).Succeeded)
            return Problems.Forbidden($"You don't have access to ticket {ticket.Number}.");

        await triage.TriageAsync(ticket, cancellationToken);
        return TypedResults.Ok(ticket.ToResponse());
    }
}
