namespace CveNet.Shared.Models;

public record CveDocument(
    string CveId,
    string Description,
    double CvssScore,
    string Severity,
    string[] AffectedProducts,
    DateOnly PublishedDate,
    string? References,
    string? Remediation);

public record CveSearchRequest(
    ParsedIntent Intent,
    int TopK = 10);

public record CveSearchResult(
    CveDocument[] Documents,
    string QueryUsed);
