using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Ticketing.Api.Features.Tickets;

namespace Ticketing.Api.Infrastructure;

public static class Roles
{
    public const string Requester = "requester";
    public const string Agent = "agent";
    public const string Admin = "admin";
}

public static class Policies
{
    /// <summary>Agents and admins: see every ticket.</summary>
    public const string Agent = "Agent";

    /// <summary>Resource-based: may this user touch this particular ticket?</summary>
    public const string TicketAccess = "TicketAccess";
}

public static class AuthExtensions
{
    /// <summary>The claim that says which customer (tenant) a user belongs to.</summary>
    public const string CustomerClaim = "customer_id";

    public static IServiceCollection AddTicketingAuth(this IServiceCollection services)
    {
        // Reads Authentication:Schemes:Bearer from configuration (dotnet user-jwts writes it)
        services.AddAuthentication().AddJwtBearer();

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Agent, p => p.RequireRole(Roles.Agent, Roles.Admin))
            .AddPolicy(Policies.TicketAccess, p => p.AddRequirements(new TicketAccessRequirement()));

        services.AddSingleton<IAuthorizationHandler, TicketAccessHandler>();
        return services;
    }

    public static bool IsAgent(this ClaimsPrincipal user) =>
        user.IsInRole(Roles.Agent) || user.IsInRole(Roles.Admin);

    /// <summary>The signed-in user's id (the token's unique_name / sub).</summary>
    public static string UserId(this ClaimsPrincipal user) => user.Identity?.Name ?? "";

    /// <summary>The signed-in user's customer. Comes from the token, never from a request body or a model.</summary>
    public static string CustomerId(this ClaimsPrincipal user) => user.FindFirstValue(CustomerClaim) ?? "";
}
