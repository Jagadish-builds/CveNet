using CveNet.Shared.Models;
using System.Diagnostics;

namespace CveNet.Orchestrator.Services;

public class OrchestratorService(
    IHttpClientFactory httpClientFactory,
    ILogger<OrchestratorService> logger)
{
    public async Task<AnalysisResponse> AnalyzeAsync(UserQuery query, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var sessionId = query.SessionId ?? Guid.NewGuid().ToString("N");

        logger.LogInformation("Starting analysis pipeline for session {SessionId}, query length {Length}",
            sessionId, query.Text.Length);

        // Step 1: Parse intent
        var intent = await CallAgentAsync<ParsedIntent>(
            "PromptParser", "/parse", query, ct);

        logger.LogInformation("Parsed intent {Intent} with {CveCount} CVE IDs for session {SessionId}",
            intent.Intent, intent.CveIds.Length, sessionId);

        // Step 2: Fan-out — search and score in parallel
        var searchRequest = new CveSearchRequest(intent);

        var (searchResult, prioritizationResult) = await FanOutAsync(intent, searchRequest, ct);

        logger.LogInformation("Fan-out complete: {DocCount} docs retrieved, {ScoredCount} scored for session {SessionId}",
            searchResult.Documents.Length, prioritizationResult.ScoredCves.Length, sessionId);

        // Step 3: Generate report
        var reportRequest = new ReportRequest(query, intent, prioritizationResult);
        var report = await CallAgentAsync<CveReport>(
            "Report", "/report", reportRequest, ct);

        sw.Stop();
        logger.LogInformation("Pipeline complete in {ElapsedMs}ms for session {SessionId}", sw.ElapsedMilliseconds, sessionId);

        return new AnalysisResponse(report, sessionId, sw.ElapsedMilliseconds);
    }

    private async Task<(CveSearchResult, PrioritizationResult)> FanOutAsync(
        ParsedIntent intent,
        CveSearchRequest searchRequest,
        CancellationToken ct)
    {
        var searchTask = CallAgentAsync<CveSearchResult>("CveSearch", "/search", searchRequest, ct);

        var searchResult = await searchTask;

        var priRequest = new PrioritizationRequest(searchResult, intent);
        var prioritizationResult = await CallAgentAsync<PrioritizationResult>("Prioritization", "/prioritize", priRequest, ct);

        return (searchResult, prioritizationResult);
    }

    private async Task<TResponse> CallAgentAsync<TResponse>(
        string agentName,
        string path,
        object payload,
        CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(agentName);

        logger.LogDebug("Calling agent {Agent} at {Path}", agentName, path);

        var response = await client.PostAsJsonAsync(path, payload, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<TResponse>(ct)
            ?? throw new InvalidOperationException($"Agent {agentName} returned null response");

        return result;
    }
}
