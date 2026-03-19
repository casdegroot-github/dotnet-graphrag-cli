> *Generated from the code intelligence graph.*

# Design Decisions

**Navigation:** [Overview](index.md) | **Design Decisions** | [Pipeline](pipeline/index.md) | [Platform](platform/index.md) | [CLI Reference](cli-reference.md)

---

Cross-cutting architectural choices and trade-offs behind GraphRagCli, grounded in the graph structure and relationships discovered during analysis.

## The graph as integration layer

The four pipeline stages ([ingest](pipeline/ingest.md), [summarize](pipeline/summarize.md), [embed](pipeline/embed.md), [search](pipeline/search.md)) share no in-memory state. Neo4j is the sole communication channel — each stage reads properties written by earlier stages and writes new ones for downstream consumers.

| Stage | Reads | Writes |
|---|---|---|
| Ingest | (nothing) | Nodes, edges, `bodyHash`, `sourceText`, `tier` |
| Summarize | `sourceText`, `tier`, `needsSummary` | `summary`, `searchText`, `tags`, `needsEmbedding` |
| Embed | `summary`, `searchText`, `needsEmbedding` | `embedding`, `pageRank`, `inDegree` |
| Search | `embedding`, `pageRank`, `summary`, `searchText` | (nothing) |

This means stages can be re-run independently — `summarize` without re-ingesting, `embed` without re-summarizing. A crashed embed run leaves the graph in a valid state, and any Neo4j client (Browser, MCP server, Cypher shell) can inspect intermediate results.

The trade-off is performance: every cross-stage data flow goes through the database. In practice this is acceptable because the bottleneck is LLM API calls, not Neo4j I/O.

## Vertical slice architecture

Features never reference other features — only `Shared/`. The graph confirms this: DI registration methods like `IngestSetup.AddIngestServices` register types within their own slice (`SolutionResolver`, `PackageResolver`, `ICodeAnalyzer`, `CodeAnalyzer`, `IngestService`), while shared infrastructure is registered separately by `AiSetup.AddAiServices` (`ModelsConfig`, `KernelFactory`), `DockerSetup.AddDockerServices` (`Neo4jContainerClient`), and `Program.Main` (`Neo4JContainerLifecycle`, `DatabaseService`, `EmbedService`).

No cross-feature REGISTERS edges exist in the graph — each slice owns its own wiring.

## Incremental processing via content hashing

Rather than tracking file timestamps or git diffs, the system hashes method bodies with SHA-256 (stripping declarations to hash only the implementation block). The graph currently has 326 nodes with `bodyHash` values — every `Embeddable` node.

This design has important consequences:

- **Renames don't trigger re-summarization** — if you rename a method but keep the body, the hash matches and the existing summary transfers to the new node via `Neo4jIngestPostProcessor`
- **Signature changes don't trigger re-summarization** — only body changes matter (see `Hasher.HashCodeBody` which strips everything before the first `{`)
- **Stale node reconciliation** uses hash matching to transfer enrichments (`summary`, `tags`, `embedding`) from old nodes to new ones, preserving expensive LLM work across refactors

The `needsSummary` and `needsEmbedding` flags propagate upward through structural relationships (`DEFINED_BY`, `BELONGS_TO_NAMESPACE`, `BELONGS_TO_PROJECT`): when a method changes, its containing class, namespace, and project are also flagged.

## Tier-based summarization order

Nodes are summarized bottom-up by architectural tier, computed via topological sort with strongly connected component (SCC) handling for cycles. The current graph spans 12 tiers:

| Tier | Node count | Typical contents |
|---|---|---|
| 0 | 127 | Leaf methods, simple types |
| 1–3 | 151 | Classes with dependencies, services |
| 4–7 | 58 | Handlers, orchestrators, repositories |
| 8–11 | 11 | Namespaces, projects, solution |

This ordering ensures the LLM always has rich child context when summarizing container nodes. Without it, a namespace summary might be generated before its classes are summarized, producing a shallow description.

SCCs (dependency cycles) are detected via GDS and grouped into a single tier, since no ordering exists within a cycle.

## Strongly-typed graph domain model

The `IGraphNode` interface (PageRank: 1.69 — the second most connected type in the graph) provides a compile-time contract that all 7 node record types implement: `SolutionNode`, `ProjectNode`, `NamespaceNode`, `ClassNode`, `InterfaceNode`, `EnumNode`, `MethodNode`.

`GraphSchema` declares every valid edge as a typed `EdgeDef(Source, RelType, Target)` triple. At runtime, `ValidateHandledRelTypes()` verifies that `PromptBuilder` handles all 11 relationship types for each node type it processes. If a new relationship type is added but not handled, the system throws `InvalidOperationException` rather than silently dropping context from summaries.

This compile-time + runtime safety net matters because the summarization pipeline's quality depends on complete relational context.

## Dual search strategy with graph reranking

[Search](pipeline/search.md) combines two retrieval methods (vector similarity and Lucene fulltext) via Reciprocal Rank Fusion, then applies graph-based reranking signals:

| Signal | Weight | Source |
|---|---|---|
| Semantic similarity | Base score | `code_embeddings` vector index (cosine) |
| Fulltext relevance | Base score (hybrid only) | `embeddable_fulltext` Lucene index, fused via RRF |
| Shared neighbors | +0.02 each | First-degree graph traversal |
| PageRank centrality | Up to +0.05 | GDS computation from [embed](pipeline/embed.md) stage |
| Entry point status | +0.10 | 5 nodes labeled `EntryPoint` during [ingest](pipeline/ingest.md) |

The system retrieves 2x the requested result count to give reranking headroom. The k=20 RRF constant dampens rank position influence, preventing a single strong match from dominating.

## Embeddable label as a gate

Not all 347 graph nodes receive embeddings. The `Embeddable` label (applied during post-processing) gates which nodes enter the vector index — currently 326 nodes. `Solution` and `Project` nodes are excluded as structural containers. This keeps the vector index focused on searchable code entities and reduces embedding costs.

## Provider abstraction for AI

The system abstracts over LLM providers via two interfaces, each with distinct provider strategies:

| Concern | Interface | Implementations | Providers |
|---|---|---|---|
| Embedding | `ITextEmbedder` | `TextEmbedder` | Ollama (local inference) |
| Summarization | `INodeSummarizer` | `ConcurrentNodeSummarizer`, `ClaudeBatchSummarizer` | Ollama, Claude |

`KernelFactory` (registered as singleton) centralizes client creation. Model configuration lives in `~/.graphragcli/models.json` with [CLI commands](cli-reference.md) for management, so switching models doesn't require code changes.

Key abstraction choices:
- **Separate document/query prefixes** on `ITextEmbedder` for retrieval-optimized models like `nomic-embed-text`
- **Claude Batch API** as a dedicated `INodeSummarizer` implementation for 50% cost reduction on large runs (activated at 100+ prompts)
- **`IChatClient` from Semantic Kernel** as the chat abstraction, with provider-specific timeout configuration (5-minute timeout for Ollama)

## Docker.DotNet over CLI shelling

Container lifecycle uses the `Docker.DotNet` library. `Neo4jContainerClient` (PageRank: 1.12) is the central service for finding and inspecting Neo4j containers, providing:

- Typed responses instead of string parsing
- Label-based container discovery (containers tagged with `graphragcli`)
- Proper error handling via exceptions rather than exit codes
- Container adoption for externally-managed Neo4j instances via a persistent registry (`~/.graphragcli/adopted.json`)

See [Database Provisioning](platform/database.md) for the full lifecycle.

## Batching strategies

Different operations use different batch sizes tuned to their constraints:

| Operation | Batch size | Rationale |
|---|---|---|
| Graph ingestion (`Neo4jIngestRepository` MERGE/UNWIND) | 100 | Balances Cypher parameter limits with throughput |
| Summary persistence (`Neo4jSummarizeRepository`) | 50 | Keeps transaction sizes manageable |
| Embedding persistence (`Neo4jEmbedRepository`) | 50 | Avoids vector parameter bloat in Cypher |
| Claude Batch API (`ClaudeBatchSummarizer`) | 1000 | Anthropic API limit per batch request |

Embedding and summarization use `SemaphoreSlim` for concurrency control rather than fixed-size task pools, allowing dynamic throughput adjustment without blocking.

## Map-reduce for oversized nodes

Nodes whose content exceeds `MaxPromptChars` can't be sent to the LLM in a single prompt. `SummarizeService` splits them by line boundaries into chunks, summarizes each chunk independently via the same `INodeSummarizer`, then reduces chunk summaries into a synthetic `NamespaceNode` for a final architectural overview. This handles large auto-generated files and sprawling namespace aggregations without truncation.

See [Summarize](pipeline/summarize.md) for the full map-reduce flow.

## MCP integration

Both `database init` and `database adopt` output a `.mcp.json` configuration template with Neo4j connection details (`NEO4J_URI`, `NEO4J_USERNAME`, `NEO4J_PASSWORD`, `NEO4J_DATABASE`). This enables Model Context Protocol clients (like Claude Code) to query the code intelligence graph directly via Cypher — the same graph these docs were generated from.

See [CLI Reference](cli-reference.md) for the full `database init` and `database adopt` command documentation.
