namespace Ticketing.Api.Ai;

/// <summary>Bound from the "Ai" section. infra/deploy.ps1 writes these to appsettings.Local.json.</summary>
public class AiOptions
{
    public string FoundryEndpoint { get; set; } = "";
    public string GatewayEndpoint { get; set; } = "";
    /// <summary>Only for demo 1, beat 1: the "demo way". Empty once local auth is off.</summary>
    public string? FoundryKey { get; set; }
    public string TriageDeployment { get; set; } = "triage";
    public string ChatDeployment { get; set; } = "chat";
    /// <summary>Who this app is, on every AI span. The Tuesday query groups by it.</summary>
    public string CallerName { get; set; } = "tickets-api";
    /// <summary>A timeout we chose on purpose. The AI step is never the critical path.</summary>
    public int TriageTimeoutSeconds { get; set; } = 20;
}
