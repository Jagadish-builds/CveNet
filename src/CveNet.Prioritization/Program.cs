using Azure.AI.OpenAI;
using CveNet.Prioritization.Services;
using CveNet.Shared.Models;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

var endpoint = builder.Configuration["AzureOpenAI:Endpoint"]!;
var apiKey = builder.Configuration["AzureOpenAI:ApiKey"]!;
var deployment = builder.Configuration["AzureOpenAI:Deployment"] ?? "gpt-35-turbo";

builder.Services.AddSingleton<IChatClient>(_ =>
    new AzureOpenAIClient(new Uri(endpoint), new System.ClientModel.ApiKeyCredential(apiKey))
        .GetChatClient(deployment)
        .AsIChatClient());

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
