using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using CveNet.Shared.Models;
using Microsoft.Extensions.AI;
using System.Text;
using System.Text.Json;

namespace CveNet.Report.Services;

public class ReportService(
    IChatClient chatClient,
    BlobContainerClient? blobContainer,
    ILogger<ReportService> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<CveReport> GenerateAsync(ReportRequest request, CancellationToken ct = default)
    {
        logger.LogInformation("Generating CVE report for intent {Intent} with {Count} scored CVEs",
            request.Intent.Intent, request.PrioritizationResult.ScoredCves.Length);

        var reportId = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

        var executiveSummary = await GenerateExecutiveSummaryAsync(request, ct);
        var recommendations = await GenerateRecommendationsAsync(request, ct);

        var report = new CveReport(
            ReportId: reportId,
            GeneratedAt: DateTimeOffset.UtcNow,
            ExecutiveSummary: executiveSummary,
            TopCves: request.PrioritizationResult.ScoredCves.Take(10).ToArray(),
            Recommendations: recommendations,
            BlobUri: null);

        string? blobUri = null;
        if (blobContainer is not null)
        {
            blobUri = await PersistReportAsync(report, reportId, ct);
            logger.LogInformation("Report {ReportId} persisted to blob: {Uri}", reportId, blobUri);
        }

        var finalReport = report with { BlobUri = blobUri };

        logger.LogInformation("Report {ReportId} generated successfully", reportId);
        return finalReport;
    }

    private async Task<string> GenerateExecutiveSummaryAsync(ReportRequest request, CancellationToken ct)
    {
        var topCves = request.PrioritizationResult.ScoredCves.Take(5).ToArray();
        var cveList = string.Join("\n", topCves.Select(s =>
            $"- {s.Document.CveId} (CVSS {s.Document.CvssScore:F1}, {s.Document.Severity}): {s.Document.Description[..Math.Min(150, s.Document.Description.Length)]}"));

        var prompt = $"""
            You are a senior security analyst writing an executive summary for a vulnerability report.

            Original query: "{request.OriginalQuery.Text}"
            Query intent: {request.Intent.Intent}
            Prioritization summary: {request.PrioritizationResult.Summary}

            Top vulnerabilities identified:
            {cveList}

            Write a clear, professional executive summary (3-4 sentences) suitable for technical leadership.
            Focus on business risk, affected systems, and urgency. Be specific about CVE IDs when relevant.
            """;

        var response = await chatClient.GetResponseAsync(prompt, cancellationToken: ct);
        return response.Text.Trim();
    }

    private async Task<string> GenerateRecommendationsAsync(ReportRequest request, CancellationToken ct)
    {
        var topCves = request.PrioritizationResult.ScoredCves.Take(5).ToArray();

        var remediations = topCves
            .Where(s => !string.IsNullOrEmpty(s.Document.Remediation))
            .Select(s => $"- {s.Document.CveId}: {s.Document.Remediation}");

        var remText = string.Join("\n", remediations);

        var prompt = $"""
            You are a security engineer providing actionable remediation recommendations.

            Known remediations from vulnerability data:
            {(string.IsNullOrEmpty(remText) ? "(none available — use general best practices)" : remText)}

            Top CVEs (by risk score): {string.Join(", ", topCves.Select(s => s.Document.CveId))}

            Provide 3-5 specific, actionable recommendations as a numbered list.
            Include patch guidance, compensating controls, and monitoring advice where relevant.
            """;

        var response = await chatClient.GetResponseAsync(prompt, cancellationToken: ct);
        return response.Text.Trim();
    }

    private async Task<string?> PersistReportAsync(CveReport report, string reportId, CancellationToken ct)
    {
        try
        {
            var json = JsonSerializer.Serialize(report, JsonOpts);
            var blobName = $"reports/{DateTimeOffset.UtcNow:yyyy/MM/dd}/{reportId}.json";
            var blobClient = blobContainer!.GetBlobClient(blobName);

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            await blobClient.UploadAsync(stream, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" }
            }, ct);

            return blobClient.Uri.ToString();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist report {ReportId} to blob storage", reportId);
            return null;
        }
    }
}
