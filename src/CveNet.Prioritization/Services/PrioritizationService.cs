using CveNet.Shared.Models;
using Microsoft.Extensions.AI;
using System.Text;
using System.Text.Json;

namespace CveNet.Prioritization.Services;

public class PrioritizationService(
    IChatClient chatClient,
    ILogger<PrioritizationService> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public async Task<PrioritizationResult> PrioritizeAsync(PrioritizationRequest request, CancellationToken ct = default)
    {
        var docs = request.SearchResult.Documents;
        logger.LogInformation("Prioritizing {Count} CVE documents", docs.Length);

        if (docs.Length == 0)
            return new PrioritizationResult([], "No CVEs found matching the query.");

        // Compute composite scores using CVSS + recency + severity weight
        var scored = docs.Select(doc => ScoreDocument(doc)).ToArray();

        // Sort by composite score descending
        Array.Sort(scored, (a, b) => b.CompositeScore.CompareTo(a.CompositeScore));

        // Use LLM to generate a natural-language summary
        var summary = await GenerateSummaryAsync(scored, request.Intent, ct);

        logger.LogInformation("Prioritization complete. Top CVE: {TopCve} (score: {Score:F2})",
            scored[0].Document.CveId, scored[0].CompositeScore);

        return new PrioritizationResult(scored, summary);
    }

    private static ScoredCve ScoreDocument(CveDocument doc)
    {
        // CVSS component: 0-10 normalized to 0-50
        double cvssComponent = (doc.CvssScore / 10.0) * 50.0;

        // Severity component: Critical=30, High=20, Medium=10, Low=5
        double severityComponent = doc.Severity.ToUpperInvariant() switch
        {
            "CRITICAL" => 30.0,
            "HIGH" => 20.0,
            "MEDIUM" => 10.0,
            "LOW" => 5.0,
            _ => 0.0
        };

        // Recency component: docs within 30 days = 20pts, 90 days = 15, 180 days = 10, else 0
        var ageInDays = (DateOnly.FromDateTime(DateTime.UtcNow) - doc.PublishedDate).TotalDays;
        double recencyComponent = ageInDays switch
        {
            <= 30 => 20.0,
            <= 90 => 15.0,
            <= 180 => 10.0,
            _ => 0.0
        };

        double composite = cvssComponent + severityComponent + recencyComponent;

        var breakdown = $"CVSS={cvssComponent:F1} Severity={severityComponent:F1} Recency={recencyComponent:F1} Total={composite:F1}";

        return new ScoredCve(doc, composite, breakdown);
    }

    private async Task<string> GenerateSummaryAsync(ScoredCve[] scored, ParsedIntent intent, CancellationToken ct)
    {
        var top5 = scored.Take(5).ToArray();
        var sb = new StringBuilder();
        sb.AppendLine("Top CVEs by composite risk score:");
        foreach (var s in top5)
            sb.AppendLine($"- {s.Document.CveId} (CVSS {s.Document.CvssScore}, {s.Document.Severity}): {s.Document.Description[..Math.Min(100, s.Document.Description.Length)]}...");

        var prompt = $"""
            You are a security analyst. Based on the following prioritized CVE list, write a concise 2-3 sentence
            executive summary of the security risk landscape relevant to the query intent: "{intent.NormalizedQuery}".
            Focus on the highest-priority items and their potential business impact.

            {sb}
            """;

        var response = await chatClient.GetResponseAsync(prompt, cancellationToken: ct);
        return response.Text.Trim();
    }
}
