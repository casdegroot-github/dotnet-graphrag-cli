# Features

Detailed breakdown of each feature slice and shared infrastructure.

## Features

### Database (`Features/Database/`)

Manages Neo4j container lifecycle: creating new containers, adopting existing ones, listing managed instances, and initializing the graph schema. Uses Docker.DotNet for container orchestration with automatic port allocation and credential management. Outputs MCP configuration JSON for AI agent integration.

Key classes:
- `DatabaseService` — core lifecycle operations (init, adopt, wait-for-ready)
- `Neo4JContainerLifecycle` — Docker image pull, container creation, port mapping
- `Neo4jSchemaService` — creates unique constraints and fulltext/vector indexes
- `OutputHelper` — generates MCP JSON configuration for Claude Code

### Ingest (`Features/Ingest/`)

Transforms C# solutions into a Neo4j knowledge graph using Roslyn for static analysis.

**Analysis** (`Ingest/Analysis/`):
- `SolutionResolver` — resolves .sln, .slnx, .csproj, .slnf files
- `CodeAnalyzer` — opens MSBuild workspace, iterates projects, distinguishes production from test code
- `CodeSyntaxWalker` — Roslyn `CSharpSyntaxWalker` that extracts classes, interfaces, enums, methods, and call relationships
- `SyntaxMapper` — converts Roslyn syntax nodes + semantic symbols into structured metadata records (`ClassInfo`, `MethodInfo`, etc.)

**GraphDb** (`Ingest/GraphDb/`):
- `Neo4jIngestRepository` — batch-upserts nodes and edges using Cypher MERGE with timestamp tracking
- `Neo4jIngestPostProcessor` — post-ingestion enrichment: body hash transfer, stale cleanup, tier computation, entry point labeling, public API labeling

### Summarize (`Features/Summarize/`)

AI-powered hierarchical summarization that processes the graph bottom-up through tiers.

**Prompts** (`Summarize/Prompts/`):
- `PromptBuilder` — generates node-type-specific instructions with provider-aware `SearchTextStrategy`
- `SummaryResult` — structured output record with `summary`, `tags`, and optional `searchText`

**Summarizers** (`Summarize/Summarizers/`):
- `ConcurrentNodeSummarizer` — parallel real-time inference with semaphore-based concurrency control and progress tracking
- `ClaudeBatchSummarizer` — submits prompts to Claude Batch API, polls for completion, parses results
- `Summarizer` — single-node summarization via Semantic Kernel chat completion with structured output

**Service**:
- `SummarizeService` — orchestrates tier-by-tier processing, applies `SearchTextStrategy` post-processing, propagates `needsSummary` flags upward

### Embed (`Features/Embed/`)

Generates vector embeddings from node summaries + searchText using configurable embedding models.

- `EmbedService` — concurrent embedding with progress tracking, vector index creation, PageRank + degree centrality via Neo4j GDS
- `Neo4jEmbedRepository` — manages embedding persistence, graph metadata, vector indexes, and GDS projections

### Search (`Features/Search/`)

Hybrid search combining Neo4j fulltext and vector indexes with graph-based reranking.

- `SearchService` — orchestrates: embed query → hybrid/vector retrieval → RRF merge → graph expansion → centrality reranking
- `Neo4jSearchRepository` — Cypher queries for vector search, fulltext search, and neighbor expansion
- `SearchResult` — result record with score, labels, neighbors, and centrality metrics

### List (`Features/List/`)

CLI inspection tool showing solutions, projects, node counts, and embedding coverage.

- `Neo4jListRepository` — queries for solution/project info with node and embedding counts

### Models (`Features/Models/`)

CLI for managing AI model configurations stored in `~/.graphragcli/models.json`.

- `ModelsAddHandler` — validates and persists new model configs
- `ModelsRemoveHandler` — removes models (prevents deleting defaults)
- `ModelsDefaultHandler` — sets default embedding/summarize model
- `ModelsListHandler` — displays all configured models

## Shared infrastructure

### AI (`Shared/Ai/`)

- `KernelFactory` — creates embedding (`ITextEmbedder`) and summarization (`Summarizer`) services for Ollama and Claude providers
- `ModelConfigLoader` — loads/saves `models.json` with embedded resource defaults and file-based persistence
- `SummarizeModelConfig` — includes `SearchTextStrategy` enum to control searchText generation per model
- `TextEmbedder` — wraps Semantic Kernel's embedding generation with prefix support

### Docker (`Shared/Docker/`)

- `Neo4jContainerClient` — container lifecycle via Docker.DotNet: inspect, list, resolve connections, track adopted containers via local JSON file
- `ResolvedConnection` — immutable connection credentials record

### GraphDb (`Shared/GraphDb/`)

- `Neo4jSessionFactory` — resolves container metadata → creates Neo4j `IDriver` with auto-detection when only one container is running

### Progress (`Shared/Progress/`)

- `ProgressHelper` — console progress bar with ETA calculation, throughput tracking, and in-place rendering