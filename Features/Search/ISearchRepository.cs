namespace GraphRagCli.Features.Search;

public interface ISearchRepository
{
    Task<List<SearchResult>> SemanticSearchAsync(float[] queryVector, int topK, string? labelFilter, CancellationToken ct = default);
    Task<List<SearchResult>> FulltextSearchAsync(string query, int topK, CancellationToken ct = default);
     Task<Dictionary<string, List<NeighborInfo>>> GetNeighborsAsync(List<string> fullNames, CancellationToken ct = default);
}