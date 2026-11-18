using Azure.Core;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Ticketing.Api.Features.Triage;

namespace Ticketing.Api.Ai;

public static class AiServiceExtensions
{
    /// <summary>The source name is the only thing that has to match between the chat clients and OpenTelemetry.</summary>
    public const string TelemetrySource = "Tickets.AI";

    public static IServiceCollection AddTicketingAi(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<AiOptions>(config.GetSection("Ai"));
        services.AddSingleton<DemoSwitches>();

        // One credential for the app. Your Entra login locally, the app's managed identity in Azure.
        services.AddSingleton<TokenCredential>(_ => new DefaultAzureCredential());
        services.AddSingleton<ModelClients>();

        // Middleware around every model call, outermost first. Same idea as the ASP.NET Core pipeline.
        AddModel(services, "triage", o => o.TriageDeployment);
        AddModel(services, "chat", o => o.ChatDeployment);

        services.AddScoped<TriageService>();
        return services;
    }

    private static void AddModel(IServiceCollection services, string key, Func<AiOptions, string> deployment)
    {
        services.AddKeyedChatClient(key, sp => new RoutedChatClient(
                sp.GetRequiredService<ModelClients>(),
                sp.GetRequiredService<DemoSwitches>(),
                deployment(sp.GetRequiredService<IOptions<AiOptions>>().Value)))
            .UseFunctionInvocation()                       // chat's tools: the model asks, our code runs them
            .UseOpenTelemetry(sourceName: TelemetrySource,
                configure: o => o.EnableSensitiveData = false)   // a data decision, not a debugging flag
            .Use((inner, sp) => new TaggingChatClient(inner,
                sp.GetRequiredService<IOptions<AiOptions>>().Value.CallerName,
                sp.GetRequiredService<DemoSwitches>()))
            .UseLogging();
    }

    /// <summary>Plug the AI spans and token metrics into the telemetry the app already has.</summary>
    public static IServiceCollection AddTicketingTelemetry(this IServiceCollection services, IConfiguration config)
    {
        var otel = services.AddOpenTelemetry()
            .WithTracing(t => t.AddSource(TelemetrySource))
            .WithMetrics(m => m.AddMeter(TelemetrySource));

        if (!string.IsNullOrWhiteSpace(config["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
            otel.UseAzureMonitor();   // reads APPLICATIONINSIGHTS_CONNECTION_STRING

        return services;
    }
}
