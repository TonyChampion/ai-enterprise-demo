using Asp.Versioning;
using Ticketing.Api.Features.Chat;
using Ticketing.Api.Features.Tickets;
using Ticketing.Api.Features.Triage;

namespace Ticketing.Api.Features;

public static class TicketingEndpoints
{
    /// <summary>The whole public surface of the API, in one place.</summary>
    public static IEndpointRouteBuilder MapTicketingEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.NewVersionedApi("Ticketing")
            .MapGroup("/api/v{version:apiVersion}")
            .HasApiVersion(new ApiVersion(1, 0))
            .RequireAuthorization();   // every endpoint needs a signed-in user

        api.MapGroup("/tickets")
            .WithTags("Tickets")
            .MapListTickets()
            .MapGetTicket()
            .MapCreateTicket()
            .MapRunTriage();

        api.MapGroup("/chat")
            .WithTags("Chat")
            .MapChat();

        return app;
    }
}
