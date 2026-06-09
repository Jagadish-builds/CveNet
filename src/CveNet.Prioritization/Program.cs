using CveNet.Prioritization.Services;
using CveNet.Shared.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

builder.Services.AddSingleton<PrioritizationService>();

var app = builder.Build();

app.MapHealthChecks("/health");

app.MapPost("/prioritize", async (PrioritizationRequest request, PrioritizationService prioritizer, CancellationToken ct) =>
{
    var result = await prioritizer.PrioritizeAsync(request, ct);
    return Results.Ok(result);
})
.WithName("Prioritize");

app.Run();
