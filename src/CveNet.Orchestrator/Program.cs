using CveNet.Orchestrator.Services;
using CveNet.Shared.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

var agentsConfig = builder.Configuration.GetSection("Agents");

void AddAgentClient(string name)
{
    var baseUrl = agentsConfig[name]
        ?? throw new InvalidOperationException($"Missing Agents:{name} configuration");
    builder.Services.AddHttpClient(name, c =>
    {
        c.BaseAddress = new Uri(baseUrl);
        c.Timeout = TimeSpan.FromSeconds(30);
    });
}

AddAgentClient("PromptParser");
AddAgentClient("CveSearch");
AddAgentClient("Prioritization");
AddAgentClient("Report");

builder.Services.AddSingleton<OrchestratorService>();

var app = builder.Build();

app.MapHealthChecks("/health");

app.MapPost("/analyze", async (UserQuery query, OrchestratorService orchestrator, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(query.Text))
        return Results.BadRequest("Query text is required.");

    if (query.Text.Length > 2000)
        return Results.BadRequest("Query must be 2000 characters or fewer.");

    var result = await orchestrator.AnalyzeAsync(query, ct);
    return Results.Ok(result);
})
.WithName("Analyze");

app.Run();
