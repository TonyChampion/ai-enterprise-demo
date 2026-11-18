using Microsoft.Extensions.Options;
using Ticketing.Api.Data;

namespace Ticketing.Api.Ai;

/// <summary>Stage controls for the demo page. Development only, and nothing here is part of the pattern.</summary>
public static class DemoEndpoints
{
    public record SwitchState(ModelRoute Route, PromptMode Prompt, ChatToolsMode ChatTools, string ModelHost, bool KeyConfigured);
    public record SwitchUpdate(ModelRoute? Route, PromptMode? Prompt, ChatToolsMode? ChatTools);

    public static IEndpointRouteBuilder MapDemoEndpoints(this IEndpointRouteBuilder app)
    {
        var demo = app.MapGroup("/demo").ExcludeFromDescription();

        demo.MapGet("/switches", State);
        demo.MapPut("/switches", (SwitchUpdate update, DemoSwitches s, ModelClients clients, IOptions<AiOptions> ai) =>
        {
            if (update.Route is { } r) s.Route = r;
            if (update.Prompt is { } p) s.Prompt = p;
            if (update.ChatTools is { } c) s.ChatTools = c;
            return State(s, clients, ai);
        });
        // Between rehearsals: seeded tickets back, switches back to the start of demo 1
        demo.MapPost("/reset", (ITicketStore store, DemoSwitches s, ModelClients clients, IOptions<AiOptions> ai) =>
        {
            (store as InMemoryTicketStore)?.Reset();
            (s.Route, s.Prompt, s.ChatTools) = (ModelRoute.Key, PromptMode.Naive, ChatToolsMode.Safe);
            return State(s, clients, ai);
        });
        return app;
    }

    private static SwitchState State(DemoSwitches s, ModelClients clients, IOptions<AiOptions> ai) =>
        new(s.Route, s.Prompt, s.ChatTools, SafeHost(clients, s.Route), !string.IsNullOrWhiteSpace(ai.Value.FoundryKey));

    private static string SafeHost(ModelClients clients, ModelRoute route)
    {
        try { return clients.HostFor(route); } catch { return "(not configured)"; }
    }
}
