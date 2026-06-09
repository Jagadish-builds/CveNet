using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using CveNet.Shared.Models;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace CveNet.CveSearch.Services;

public class CveSearchService(
    SearchClient searchClient,
    IChatClient chatClient,
    ILogger<CveSearchService> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public async Task<CveSearchResult> SearchAsync(CveSearchRequest request, CancellationToken ct = default)
    {
        var intent = request.Intent;
        logger.LogInformation("Searching CVEs for intent {Intent}, keywords: {Keywords}",
            intent.Intent, string.Join(", ", intent.Keywords));

        // Build search query from intent
        string searchQuery = BuildSearchQuery(intent);

        var searchOptions = new SearchOptions
        {
            Size = request.TopK,
            Select = {
                "cveId", "description", "cvssScore",
                "severity", "vendor", "product",
                "published", "references"
            }
        };

        if (intent.Severity is not null)
            searchOptions.Filter = $"severity eq '{intent.Severity}'";

        if (intent.CveIds.Length > 0)
        {
            var idFilter = string.Join(" or ",
                intent.CveIds.Select(id => $"cveId eq '{id}'"));
            searchOptions.Filter = searchOptions.Filter is null
                ? idFilter
                : $"({searchOptions.Filter}) and ({idFilter})";
        }

        logger.LogDebug("Azure AI Search query: {Query}, filter: {Filter}", searchQuery, searchOptions.Filter);

        List<CveDocument> docs;
        try
        {
            var results = await searchClient.SearchAsync<SearchDocument>(searchQuery, searchOptions, ct);
            docs = new List<CveDocument>();
            await foreach (var result in results.Value.GetResultsAsync())
            {
                var doc = MapSearchDocument(result.Document);
                if (doc is not null)
                    docs.Add(doc);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Azure AI Search failed: {Message}", ex.Message);
            throw;
        }

        logger.LogInformation("Retrieved {Count} CVE documents from Azure AI Search", docs.Count);

        // If no docs found and we have CVE IDs, use LLM fallback to describe them
        if (docs.Count == 0 && intent.CveIds.Length > 0)
        {
            docs = await FallbackLlmSearchAsync(intent.CveIds, ct);
        }

        return new CveSearchResult(docs.ToArray(), searchQuery);
    }

    private static string BuildSearchQuery(ParsedIntent intent)
    {
        if (intent.CveIds.Length > 0)
            return string.Join(" OR ", intent.CveIds);

        var parts = new List<string>(intent.Keywords);
        if (!string.IsNullOrEmpty(intent.ProductName))
            parts.Add(intent.ProductName);

        return parts.Count > 0 ? string.Join(" ", parts) : "*";
    }

    private static CveDocument? MapSearchDocument(SearchDocument doc)
    {
        try
        {
            var vendor = doc.GetString("vendor");
            var product = doc.GetString("product");
            var affectedProducts = new[] { vendor, product }
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => s!)
                .ToArray();

            DateOnly publishedDate = DateOnly.MinValue;
            if (doc.TryGetValue("published", out var pubVal))
            {
                if (pubVal is DateTimeOffset dto)
                    publishedDate = DateOnly.FromDateTime(dto.DateTime);
                else if (pubVal is string s && DateOnly.TryParse(s, out var d))
                    publishedDate = d;
            }

            return new CveDocument(
                CveId: doc.GetString("cveId") ?? string.Empty,
                Description: doc.GetString("description") ?? string.Empty,
                CvssScore: doc.TryGetValue("cvssScore", out var score) ? Convert.ToDouble(score) : 0.0,
                Severity: doc.GetString("severity") ?? "Unknown",
                AffectedProducts: affectedProducts,
                PublishedDate: publishedDate,
                References: doc.GetString("references"),
                Remediation: null);
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or KeyNotFoundException)
        {
            return null;
        }
    }

    private async Task<List<CveDocument>> FallbackLlmSearchAsync(string[] cveIds, CancellationToken ct)
    {
        logger.LogInformation("No index results found; using LLM fallback for {Count} CVE IDs", cveIds.Length);

        var prompt = $$"""
            Provide structured JSON descriptions for these CVE IDs: {{string.Join(", ", cveIds)}}.
            Return a JSON array where each element has:
            {"cveId": "", "description": "", "cvssScore": 0.0, "severity": "Critical|High|Medium|Low",
              "affectedProducts": [], "publishedDate": "YYYY-MM-DD", "references": "", "remediation": ""}
            Use your training knowledge. If unknown, estimate conservatively.
            """;

        var response = await chatClient.GetResponseAsync(prompt, cancellationToken: ct);
        var json = response.Text.Trim();

        if (json.StartsWith("```"))
        {
            json = json.Split('\n', 2)[1];
            json = json[..json.LastIndexOf("```", StringComparison.Ordinal)].Trim();
        }

        var raw = JsonSerializer.Deserialize<RawCveDoc[]>(json, JsonOpts) ?? [];
        return raw.Select(r => new CveDocument(
            r.CveId ?? string.Empty,
            r.Description ?? string.Empty,
            r.CvssScore,
            r.Severity ?? "Unknown",
            r.AffectedProducts ?? [],
            DateOnly.TryParse(r.PublishedDate, out var d) ? d : DateOnly.MinValue,
            r.References,
            r.Remediation)).ToList();
    }

    private record RawCveDoc(
        string? CveId,
        string? Description,
        double CvssScore,
        string? Severity,
        string[]? AffectedProducts,
        string? PublishedDate,
        string? References,
        string? Remediation);
}

file static class SearchDocumentExtensions
{
    public static string? GetString(this SearchDocument doc, string key) =>
        doc.TryGetValue(key, out var v) ? v?.ToString() : null;
}
