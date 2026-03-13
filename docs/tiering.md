# Tiering & hierarchical summarization

## How tiers are computed

Tiers are computed by `Neo4jIngestPostProcessor.ComputeTiersAsync()` which finds the longest incoming path to each node:

```cypher
MATCH (n)
OPTIONAL MATCH path = ()-[*1..20]->(n)
WITH n, COALESCE(max(length(path)), 0) AS tier
SET n.tier = tier
```

A node's tier = the length of the longest chain of relationships pointing to it. Leaf nodes (no incoming edges) are tier 0. Nodes that are pointed to by tier-0 nodes are tier 1, and so on.

## Tier distribution

| Tier | Typical contents | Examples |
|------|----------|---------|
| 0 | Leaf methods, standalone classes, enums | `Hasher.ComputeHash()`, `SearchMode` enum |
| 1 | Classes/methods with one level of children, small namespaces | `DatabaseOption`, `Shared.Options` namespace |
| 2 | Classes with deeper children, mid-level namespaces | `ProgressHelper`, `Shared.Progress` namespace |
| 3 | Aggregating classes, feature-root namespaces | `Shared` namespace |
| 4-5 | Feature namespaces, service classes | `Ingest.Analysis`, `Shared.Docker`, `IngestService` |
| 6-8 | Top-level feature namespaces | `Features.Search`, `Features.Summarize`, `Features.Database` |
| 10 | Solution | `GraphRagCli` (solution) |
| 11 | Project | `GraphRagCli` (project) |

## How summarization uses tiers

Summarization processes tiers bottom-up (0 → 11). When summarizing a tier-N node, all its children (tier < N) already have summaries. These child summaries are included in the prompt as context:

```
Summarize this namespace for a code intelligence graph.
Given the components and their summaries, explain: ...

GraphRagCli.Features.Search

Components:
- SearchService: Enables intelligent code search by combining...
- Neo4jSearchRepository: Enables intelligent code search and navigation...
- SearchCommandHandler: Enables semantic code search and retrieval...
```

This cascading context means higher-tier summaries capture the full picture of their children without needing to read source code directly.

## SearchText strategy

Each model configures how `searchText` is generated via `SearchTextStrategy` in `models.json`:

- **`separate`** (Claude): The LLM produces `searchText` as a distinct field — keyword-dense, optimized for vector search retrieval.
- **`firstTwoSentences`** (Ollama): The prompt instructs the model to front-load keyword-dense sentences. After summarization, `searchText` is extracted by taking the first two sentences from the summary.

This matters because Ollama models tend to produce `searchText` identical to the summary (or low-quality keyword stuffing), while Claude reliably produces distinct, high-quality searchText.

## Prompt structure by node type

| Node type | Instruction focus | Sentence limit |
|-----------|------------------|----------------|
| Method | Business problem, data flow, decisions | 2-4 |
| Method (EntryPoint) | What subsystem it wires up, what services it registers | 2-4 |
| Class | Business problem, data flow, orchestration pattern | 2-4 |
| Interface | Capability abstracted, why the boundary exists | 2-4 |
| Enum | Domain concept, member explanations, consumer behavior | 2-4 |
| Namespace | Business capability, data flow, component collaboration | 3-5 |
| Project | Core purpose, workflows, namespace layering | 3-5 |
| Solution | Elevator pitch for LLM routing | 1-2 |