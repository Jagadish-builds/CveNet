using Azure.AI.OpenAI;
using Azure.Storage.Blobs;
using CveNet.Report.Services;
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

// Blob container is optional — gracefully degrade if connection string is absent
var blobConnectionString = builder.Configuration["AzureStorage:ConnectionString"];
if (!string.IsNullOrEmpty(blobConnectionString))
{
    builder.Services.AddSingleton(_ =>
    {
        var serviceClient = new BlobServiceClient(blobConnectionString);
        var container = serviceClient.GetBlobContainerClient("cvenet-reports");
        container.CreateIfNotExists();
        return container;
    });
}
else
{
    builder.Services.AddSingleton<BlobContainerClient?>(_ => null);
}

builder.Services.AddSingleton<ReportService>();

var app = builder.Build();

app.MapHealthChecks("/health");

app.MapPost("/report", async (ReportRequest request, ReportService reporter, CancellationToken ct) =>
{
    var result = await reporter.GenerateAsync(request, ct);
    return Results.Ok(result);
})
.WithName("Report");

app.Run();
