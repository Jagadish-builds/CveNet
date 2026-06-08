using Azure;
using Azure.AI.OpenAI;
using Azure.Search.Documents;
using CveNet.CveSearch.Services;
using CveNet.Shared.Models;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

var oaiEndpoint = builder.Configuration["AzureOpenAI:Endpoint"]!;
var oaiKey = builder.Configuration["AzureOpenAI:ApiKey"]!;
var deployment = builder.Configuration["AzureOpenAI:Deployment"] ?? "gpt-35-turbo";

var searchEndpoint = builder.Configuration["AzureSearch:Endpoint"]!;
var searchKey = builder.Configuration["AzureSearch:ApiKey"]!;
var indexName = builder.Configuration["AzureSearch:IndexName"] ?? "cvenet-index";

builder.Services.AddSingleton<IChatClient>(_ =>
    new AzureOpenAIClient(new Uri(oaiEndpoint), new System.ClientModel.ApiKeyCredential(oaiKey))
        .GetChatClient(deployment)
        .AsIChatClient());

builder.Services.AddSingleton(_ =>
    new SearchClient(new Uri(searchEndpoint), indexName, new AzureKeyCredential(searchKey)));

builder.Services.AddSingleton<CveSearchService>();

var app = builder.Build();

app.MapHealthChecks("/health");

app.MapPost("/search", async (CveSearchRequest request, CveSearchService searcher, CancellationToken ct) =>
{
    var result = await searcher.SearchAsync(request, ct);
    return Results.Ok(result);
})
.WithName("Search");

app.Run();
