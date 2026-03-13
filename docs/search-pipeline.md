# Search pipeline

## Overview

The search pipeline transforms a natural language query into ranked code intelligence results through four stages: embedding, retrieval, graph expansion, and reranking.

## 1. Query embedding

The user's query string is embedded into a vector using the configured embedding model (e.g. `qwen3-embedding:4b`). The model's `queryPrefix` is prepended to the query before embedding to optimize for retrieval.

## 2. Candidate retrieval

### Hybrid mode (default)

Runs fulltext and vector search in parallel against Neo4j, then merges with Reciprocal Rank Fusion (RRF):

```
rrfScore = 0.5 / (k + fulltextRank) + 0.5 / (k + vectorRank)
```

where `k = 20`. Nodes appearing in both result sets get a combined score boost. Retrieves `topK * 2` candidates to allow headroom for reranking.

The fulltext index searches against `fullName` and `summary`. The vector index searches against the `embedding` property (generated from `searchText` content).

### Vector mode

Runs only the Neo4j vector index search. Useful when the query is conceptual rather than name-based.

## 3. Graph expansion

For each candidate, `GetNeighborsAsync` fetches immediate neighbors via all relationship types. This surfaces related code that the user didn't directly search for:

- Find a method → see its parent class, its callers, and what it references
- Find a class → see its namespace, interfaces it implements, methods it defines
- Find an interface → see all implementing classes

Neighbors are returned with their summary, relationship type, and full name. They appear in the search output as indented sub-results.

## 4. Reranking

Final scores are computed by adding graph-based bonuses to the retrieval score:

| Signal | Bonus | Description |
|--------|-------|-------------|
| **Shared neighbors** | `+0.02` per shared neighbor | If two candidates share neighbors, they're likely related — boost both |
| **PageRank centrality** | up to `+0.05` | Central nodes in the graph rank higher (capped) |
| **Entry point** | `+0.10` | DI registration methods get a fixed boost (high-value navigation targets) |

Results are sorted by final score and truncated to `topK`.

## Example

```bash
$ dotnet run -- search "docker container management" --top 3

Score    Type    Name                                               Summary
0.3929  Method  Neo4jContainerClient.TryInspectAsync(string)       Resilient container inspection...
         └─ CALLED_BY  ResolveAsync          Resolves Neo4j database connection...
         └─ CALLED_BY  GetStatusAsync        Resilient Neo4j database container...
0.3561  Method  Neo4jContainerClient.CollectAllNamesAsync()        Collects all container identifiers...
         └─ CALLED_BY  ListWithStatusAsync   Orchestrates container status...
0.3516  Method  Neo4jContainerClient.ParseBoltPort(...)            Extracts Neo4j port from Docker...
         └─ CALLED_BY  ResolveAsync          Resolves Neo4j database connection...
```