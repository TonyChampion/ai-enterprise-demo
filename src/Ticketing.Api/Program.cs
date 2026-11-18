using Ticketing.Api.Ai;
using Ticketing.Api.Features;
using Ticketing.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Written by infra/deploy.ps1. Gitignored: it can hold the demo key.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Services.AddApiDefaults();
builder.Services.AddErrorHandling();
builder.Services.AddTicketing();
builder.Services.AddTicketingAuth();
builder.Services.AddTicketingAi(builder.Configuration);
builder.Services.AddTicketingTelemetry(builder.Configuration);
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseErrorHandling();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi().WithDocumentPerVersion();
if (app.Environment.IsDevelopment())
{
    app.UseSwaggerUI(o => o.SwaggerEndpoint("/openapi/v1.json", "v1"));
    app.MapDemoEndpoints();
}
app.MapTicketingEndpoints();
app.MapHealthChecks("/health");

app.Run();

public partial class Program;
