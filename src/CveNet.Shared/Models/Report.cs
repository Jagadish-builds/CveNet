namespace CveNet.Shared.Models;

public record ReportRequest(
    UserQuery OriginalQuery,
    ParsedIntent Intent,
    PrioritizationResult PrioritizationResult);

public record CveReport(
    string ReportId,
    DateTimeOffset GeneratedAt,
    string ExecutiveSummary,
    ScoredCve[] TopCves,
    string Recommendations,
    string? BlobUri);

public record AnalysisResponse(
    CveReport Report,
    string SessionId,
    long ElapsedMs);
