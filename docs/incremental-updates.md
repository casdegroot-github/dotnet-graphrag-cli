# Incremental updates

The system is designed for repeated runs where only changed code needs reprocessing. Each stage tracks what has changed and propagates invalidation upward through the graph.

## Ingest: timestamp + body hash

Every node and edge gets a `lastIngestedAt` timestamp on each run. After ingestion, the post-processor runs four reconciliation steps:

### 1. Body hash comparison

During Cypher MERGE, each node's `bodyHash` (SHA-256 of its source text) is compared to the existing value:

```cypher
SET c.needsSummary = CASE
    WHEN c.bodyHash IS NULL OR c.bodyHash <> item.bodyHash
    THEN true ELSE c.needsSummary END,
    c.bodyHash = item.bodyHash,
    c.lastIngestedAt = $runTimestamp
```

If the hash changed, `needsSummary` is flagged. If unchanged, the existing flag is preserved.

### 2. Property transfer

`TransferByBodyHashAsync` finds stale nodes (old `lastIngestedAt`) that match fresh nodes by `bodyHash`. This handles code that was renamed or moved but not changed:

```cypher
MATCH (stale) WHERE stale.lastIngestedAt < $runTimestamp AND stale.bodyHash IS NOT NULL
MATCH (fresh) WHERE fresh.lastIngestedAt >= $runTimestamp AND fresh.bodyHash = stale.bodyHash
SET fresh.summary = stale.summary, fresh.searchText = stale.searchText,
    fresh.tags = stale.tags, fresh.embedding = stale.embedding
```

This avoids expensive re-summarization when code is refactored without changing behavior.

### 3. Stale cleanup

- `DeleteStaleEdgesAsync`: removes edges with old timestamps (deleted relationships)
- `DeleteStaleNodesAsync`: removes nodes with old timestamps (deleted code). Also marks dependent nodes' ancestors as stale so they get re-summarized.

### 4. Tier recomputation

Tiers are recomputed after every ingest since the graph structure may have changed (new nodes, deleted edges).

## Summarize: needsSummary flag

`GetTierNodesAsync` fetches nodes where `needsSummary = true` (or all nodes if `--force`). After summarizing a node:

1. `needsSummary` is set to `false`
2. `needsEmbedding` is set to `true` (summary changed → embedding is stale)
3. **Upward propagation**: all direct parents get `needsSummary = true`

```cypher
MATCH (n)-[]->(parent)
WHERE n.elementId IN $elementIds
SET parent.needsSummary = true
```

This cascade ensures that a change in a leaf method eventually propagates re-summarization up through its class, namespace, project, and solution.

## Embed: needsEmbedding flag

`EmbedService` fetches nodes where `needsEmbedding = true` (or all if `--force`). After embedding, the flag is cleared and `embeddingHash` is set to the current `bodyHash`. The Neo4j vector index is automatically maintained.

## Typical incremental workflow

```bash
# Developer changes some code, then:
dotnet run -- ingest /path/to/Solution.sln    # only changed nodes get needsSummary=true
dotnet run -- summarize                        # only dirty nodes + propagated parents
dotnet run -- embed                            # only nodes with new summaries
```

## What triggers re-processing

| Change | Ingest effect | Summarize effect | Embed effect |
|--------|--------------|-----------------|--------------|
| Method body changed | `needsSummary = true` on method | Method re-summarized, parent class/namespace flagged | Method re-embedded |
| Class renamed (same body) | Property transfer from old → new node | No re-summarization needed | No re-embedding needed |
| Method deleted | Stale node removed, parent flagged | Parent re-summarized | Parent re-embedded |
| New method added | New node with `needsSummary = true` | Method summarized, parent flagged | Method embedded |
| No code changes | No flags set | Nothing to process | Nothing to process |