using Microsoft.AspNetCore.Authorization;
using Ticketing.Api.Domain;
using Ticketing.Api.Infrastructure;

namespace Ticketing.Api.Features.Tickets;

/// <summary>"May this user touch this ticket?" The answer depends on the ticket, not just the user.</summary>
public class TicketAccessRequirement : IAuthorizationRequirement;

public class TicketAccessHandler : AuthorizationHandler<TicketAccessRequirement, Ticket>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, TicketAccessRequirement requirement, Ticket ticket)
    {
        // Agents can touch any ticket; customers only their own company's
        if (context.User.IsAgent() || ticket.CustomerId == context.User.CustomerId())
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
