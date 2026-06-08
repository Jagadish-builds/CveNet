using Azure.AI.OpenAI;
using CveNet.PromptParser.Services;
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

builder.Services.AddSingleton<PromptParserService>();

var app = builder.Build();

app.MapHealthChecks("/health");

app.MapPost("/parse", async (UserQuery query, PromptParserService parser, CancellationToken ct) =>
{
    var result = await parser.ParseAsync(query, ct);
    return Results.Ok(result);
})
.WithName("Parse");

app.Run();
