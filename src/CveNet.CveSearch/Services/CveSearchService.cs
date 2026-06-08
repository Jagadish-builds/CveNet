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

        var options = new SearchOptions
        {
            Size = request.MaxResults,
            Select = {
                "cveId", "description", "cvssScore", "severity",
                "affectedProducts", "publishedDate", "references", "remediation"
            }
        };

        // Apply severity filter if specified
        if (!string.IsNullOrEmpty(intent.Severity))
            options.Filter = $"severity eq '{intent.Severity}'";

        logger.LogDebug("Azure AI Search query: {Query}, filter: {Filter}", searchQuery, options.Filter);

        var results = await searchClient.SearchAsync<SearchDocument>(searchQuery, options, ct);

        var docs = new List<CveDocument>();
        await foreach (var result in results.Value.GetResultsAsync())
        {
            var doc = MapSearchDocument(result.Document);
            if (doc is not null)
                docs.Add(doc);
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
            return new CveDocument(
                CveId: doc.GetString("cveId") ?? string.Empty,
                Description: doc.GetString("description") ?? string.Empty,
                CvssScore: doc.TryGetValue("cvssScore", out var score) ? Convert.ToDouble(score) : 0.0,
                Severity: doc.GetString("severity") ?? "Unknown",
                AffectedProducts: doc.TryGetValue("affectedProducts", out var prods) && prods is IEnumerable<object> list
                    ? list.Select(p => p.ToString() ?? "").ToArray()
                    : [],
                PublishedDate: doc.TryGetValue("publishedDate", out var date) && date is DateTimeOffset dto
                    ? DateOnly.FromDateTime(dto.DateTime)
                    : DateOnly.MinValue,
                References: doc.GetString("references"),
                Remediation: doc.GetString("remediation"));
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or KeyNotFoundException)
        {
            return null;
        }
    }

    private async Task<List<CveDocument>> FallbackLlmSearchAsync(string[] cveIds, CancellationToken ct)
    {
        logger.LogInformation("No index results found; using LLM fallback for {Count} CVE IDs", cveIds.Length);

        var prompt = $"""
            Provide structured JSON descriptions for these CVE IDs: {string.Join(", ", cveIds)}.
            Return a JSON array where each element has:
            {{"cveId": "", "description": "", "cvssScore": 0.0, "severity": "Critical|High|Medium|Low",
              "affectedProducts": [], "publishedDate": "YYYY-MM-DD", "references": "", "remediation": ""}}
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
