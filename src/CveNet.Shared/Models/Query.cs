namespace CveNet.Shared.Models;

public record UserQuery(
    string Text,
    string? SessionId = null);

public record DateRange(
    DateOnly? From,
    DateOnly? To);

public enum QueryIntent
{
    Unknown,
    LookupCve,
    SearchByProduct,
    SearchBySeverity,
    RecentVulnerabilities,
    ThreatSummary
}

public record ParsedIntent(
    QueryIntent Intent,
    string[] Keywords,
    string[] CveIds,
    string? ProductName,
    string? Severity,
    DateRange? DateRange,
    string NormalizedQuery);
