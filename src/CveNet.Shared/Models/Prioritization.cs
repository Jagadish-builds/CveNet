namespace CveNet.Shared.Models;

public record ScoredCve(
    CveDocument Document,
    double CompositeScore,
    string ScoreBreakdown);

public record PrioritizationRequest(
    CveSearchResult SearchResult,
    ParsedIntent Intent);

public record PrioritizationResult(
    ScoredCve[] ScoredCves,
    string Summary);
