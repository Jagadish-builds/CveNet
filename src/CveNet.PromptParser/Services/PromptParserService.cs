using CveNet.Shared.Models;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace CveNet.PromptParser.Services;

public class PromptParserService(
    IChatClient chatClient,
    ILogger<PromptParserService> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public async Task<ParsedIntent> ParseAsync(UserQuery query, CancellationToken ct = default)
    {
        logger.LogInformation("Parsing intent for query: {Query}", query.Text);

        var systemPrompt = """
            You are a CVE security query parser. Analyze the user's query and extract structured information.
            Respond ONLY with valid JSON matching this schema:
            {
              "intent": "<Unknown|LookupCve|SearchByProduct|SearchBySeverity|RecentVulnerabilities|ThreatSummary>",
              "keywords": ["<keyword>"],
              "cveIds": ["<CVE-YYYY-NNNNN>"],
              "productName": "<product or null>",
              "severity": "<Critical|High|Medium|Low or null>",
              "dateRange": { "from": "<YYYY-MM-DD or null>", "to": "<YYYY-MM-DD or null>" },
              "normalizedQuery": "<clean rephrasing of the query>"
            }
            Return null for optional fields when not applicable. Return empty arrays for keywords/cveIds when none found.
            """;

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, query.Text)
        };

        var response = await chatClient.GetResponseAsync(messages, cancellationToken: ct);
        var json = response.Text.Trim();

        // Strip markdown code fences if present
        if (json.StartsWith("```"))
        {
            json = json.Split('\n', 2)[1];
            json = json[..json.LastIndexOf("```", StringComparison.Ordinal)].Trim();
        }

        logger.LogDebug("LLM returned intent JSON: {Json}", json);

        var raw = JsonSerializer.Deserialize<RawIntent>(json, JsonOpts)
            ?? throw new InvalidOperationException("LLM returned null intent");

        return MapToIntent(raw);
    }

    private static ParsedIntent MapToIntent(RawIntent raw)
    {
        var intent = Enum.TryParse<QueryIntent>(raw.Intent, out var parsed) ? parsed : QueryIntent.Unknown;

        DateRange? dateRange = null;
        if (raw.DateRange is { From: not null } or { To: not null })
        {
            DateOnly.TryParse(raw.DateRange?.From, out var from);
            DateOnly.TryParse(raw.DateRange?.To, out var to);
            dateRange = new DateRange(
                from == default ? null : from,
                to == default ? null : to);
        }

        return new ParsedIntent(
            Intent: intent,
            Keywords: raw.Keywords ?? [],
            CveIds: raw.CveIds ?? [],
            ProductName: raw.ProductName,
            Severity: raw.Severity,
            DateRange: dateRange,
            NormalizedQuery: raw.NormalizedQuery ?? string.Empty);
    }

    private record RawIntent(
        string Intent,
        string[]? Keywords,
        string[]? CveIds,
        string? ProductName,
        string? Severity,
        RawDateRange? DateRange,
        string? NormalizedQuery);

    private record RawDateRange(string? From, string? To);
}
