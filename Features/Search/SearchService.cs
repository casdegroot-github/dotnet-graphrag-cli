using GraphRagCli.Shared;
using GraphRagCli.Shared.Ai;

namespace GraphRagCli.Features.Search;

public class SearchService(ISearchRepository repository, ITextEmbedder embedder)
{
    public async Task<List<SearchResult>> SearchAsync(
        string query, SearchMode mode, int topK, string? typeFilter, CancellationToken ct = default)
    {
        var vector = await embedder.EmbedQueryAsync(query);

        var candidates = mode == SearchMode.Vector
            ? await repository.SemanticSearchAsync(vector, topK * 2, typeFilter, ct)
            : await HybridSearchAsync(query, vector, topK * 2, ct);

        return await GraphExpandAndRerankAsync(candidates, topK, ct);
    }

    private async Task<List<SearchResult>> HybridSearchAsync(
        string query, float[] vector, int topK, CancellationToken ct)
    {
        var fulltextTask = repository.FulltextSearchAsync(query, topK, ct);
        var vectorTask = repository.SemanticSearchAsync(vector, topK, null, ct);
        await Task.WhenAll(fulltextTask, vectorTask);

        var fulltextResults = fulltextTask.Result;
        var vectorResults = vectorTask.Result;

        const int k = 20;
        var fulltextRanks = fulltextResults.Select((r, i) => (r.FullName, Rank: i + 1)).ToDictionary(x => x.FullName, x => x.Rank);
        var vectorRanks = vectorResults.Select((r, i) => (r.FullName, Rank: i + 1)).ToDictionary(x => x.FullName, x => x.Rank);

        return fulltextResults.Concat(vectorResults)
            .GroupBy(r => r.FullName)
            .Select(g =>
            {
                var best = g.First();
                var ftRank = fulltextRanks.GetValueOrDefault(g.Key, topK + 1);
                var vecRank = vectorRanks.GetValueOrDefault(g.Key, topK + 1);
                var rrfScore = 0.5 / (k + ftRank) + 0.5 / (k + vecRank);
                return best with { Score = rrfScore };
            })
            .OrderByDescending(r => r.Score)
            .Take(topK)
            .ToList();
    }

    private async Task<List<SearchResult>> GraphExpandAndRerankAsync(
        List<SearchResult> candidates, int topK, CancellationToken ct)
    {
        if (candidates.Count == 0) return [];

        var fullNames = candidates.Select(c => c.FullName).ToList();
        var neighborsMap = await repository.GetNeighborsAsync(fullNames, ct);

        return candidates.Select(c =>
        {
            var neighbors = neighborsMap.GetValueOrDefault(c.FullName);
            var myNeighborFns = neighbors?.Select(n => n.FullName).Where(fn => fn != null).ToHashSet() ?? [];

            var graphBonus = 0.0;
            foreach (var other in fullNames)
            {
                if (other == c.FullName) continue;
                if (!neighborsMap.TryGetValue(other, out var otherNeighbors)) continue;
                var otherFns = otherNeighbors.Select(n => n.FullName).Where(fn => fn != null).ToHashSet();
                graphBonus += myNeighborFns.Intersect(otherFns).Count() * 0.02;
            }

            var centralityBonus = Math.Min((c.PageRank ?? 0) * 0.1, 0.05);
            var entryPointBonus = c.Labels?.Contains(NodeLabels.EntryPoint) == true ? 0.10 : 0.0;

            return c with
            {
                Score = c.Score + graphBonus + centralityBonus + entryPointBonus,
                Neighbors = neighbors
            };
        })
        .OrderByDescending(c => c.Score)
        .Take(topK)
        .ToList();
    }
}
