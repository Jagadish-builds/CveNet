using CveNet.Shared.Models;

namespace CveNet.Prioritization.Services;

public class PrioritizationService(ILogger<PrioritizationService> logger)
{
    public Task<PrioritizationResult> PrioritizeAsync(PrioritizationRequest request, CancellationToken ct = default)
    {
        var docs = request.SearchResult.Documents;
        logger.LogInformation("Prioritizing {Count} CVE documents", docs.Length);

        if (docs.Length == 0)
            return Task.FromResult(new PrioritizationResult([], "No CVEs found matching the query."));

        var scored = docs.Select(doc => ScoreDocument(doc)).ToArray();
        Array.Sort(scored, (a, b) => b.CompositeScore.CompareTo(a.CompositeScore));

        logger.LogInformation("Prioritization complete. Top CVE: {TopCve} (score: {Score:F2})",
            scored[0].Document.CveId, scored[0].CompositeScore);

        return Task.FromResult(new PrioritizationResult(scored, string.Empty));
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
        var ageInDays = DateOnly.FromDateTime(DateTime.UtcNow).DayNumber - doc.PublishedDate.DayNumber;
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


}
