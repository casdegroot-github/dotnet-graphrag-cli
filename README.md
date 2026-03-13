# GraphRAG CLI

A .NET CLI tool that builds a code intelligence graph from C# solutions using Roslyn, Neo4j, and LLM-powered summarization. Supports semantic search, graph traversal, and integration with Claude Code via MCP.

## Prerequisites

- .NET 10 SDK
- Docker (for Neo4j containers)
- Ollama (for local embeddings + optional local summarization)
- Anthropic API key (optional, for Claude summarization)

## Quick start

```bash
# 1. Create a Neo4j database
dotnet run -- database init

# 2. Ingest a C# solution
dotnet run -- ingest /path/to/MySolution.sln

# 3. Summarize with an LLM
dotnet run -- summarize

# 4. Generate embeddings
dotnet run -- embed

# 5. Search the graph
dotnet run -- search "how does retry work"
```

## Setup

### Ollama (required)

Ollama runs the embedding model for all search operations.

```bash
brew install ollama
ollama serve
ollama pull qwen3-embedding:4b    # embedding model
ollama pull qwen3-coder           # summarization model (optional)
```

### Claude (optional)

For higher-quality summaries using Claude:

```bash
export ANTHROPIC_API_KEY=sk-ant-...
```

Claude produces better summaries but costs money. Supports the Batch API for 50% cost reduction.

## Commands

### `database` — Manage Neo4j containers

```bash
# Create a new Neo4j container
dotnet run -- database init
dotnet run -- database init --name my-project --port 7688

# List all managed containers
dotnet run -- database list

# Adopt an existing Neo4j container
dotnet run -- database adopt my-container
```

### `ingest` — Parse C# code into the graph

```bash
dotnet run -- ingest /path/to/MySolution.sln
dotnet run -- ingest /path/to/MySolution.sln --skip-tests --skip-samples
dotnet run -- ingest /path/to/MySolution.sln --nuget-slnf /path/to/nuget.slnf
```

Parses all C# code with Roslyn: extracts classes, interfaces, enums, methods, call relationships, and type references. Creates nodes and edges in Neo4j. Incremental — only re-processes changed code.

### `summarize` — Generate LLM summaries

```bash
# With Ollama (free, local)
dotnet run -- summarize

# With Claude (faster, better quality)
dotnet run -- summarize --model claude-haiku-4-5-20251001

# With Claude Batch API (50% cheaper, async)
dotnet run -- summarize --model claude-haiku-4-5-20251001 --batch

# Test with a small sample first
dotnet run -- summarize --sample --force

# Only process specific tiers
dotnet run -- summarize --tier 0 --tier 1

# Show tier breakdown
dotnet run -- summarize --list-tiers
```

Summarizes nodes bottom-up through tiers: methods first, then classes, namespaces, projects, and the solution. Each tier's summaries feed into the next tier's prompts.

| Option | Description |
|--------|-------------|
| `--model` | Summarization model (default: from `~/.graphragcli/models.json`) |
| `--force` | Mark all nodes for re-summarization (resumable if interrupted) |
| `--batch` | Use Claude Batch API (Claude models only, ≥100 nodes per tier) |
| `--sample` | Test with 1 node per type |
| `--tier N` | Only process specific tiers (repeatable) |
| `--list-tiers` | Show tier breakdown and exit |
| `--prompt` | Custom prompt instruction (overrides default) |

### `embed` — Generate vector embeddings

```bash
dotnet run -- embed
dotnet run -- embed --force          # re-embed everything
dotnet run -- embed --model nomic-embed-text
```

Generates vector embeddings from summaries + searchText, creates a Neo4j vector index, and computes PageRank centrality scores for search ranking.

| Option | Description |
|--------|-------------|
| `--model` | Embedding model (default: from `~/.graphragcli/models.json`) |
| `--force` | Re-embed all nodes |

### `search` — Query the code graph

```bash
dotnet run -- search "docker container management"
dotnet run -- search "how does retry work" --top 5
dotnet run -- search "input pipeline" --type Class
dotnet run -- search "change detection" --mode Vector
```

Hybrid search combines Neo4j fulltext and vector indexes, then reranks using graph relationships (neighbors, centrality).

| Option | Description |
|--------|-------------|
| `--top N` | Number of results (default: 10) |
| `--mode` | `Hybrid` (default) or `Vector` |
| `--type` | Filter: `Class`, `Interface`, `Method`, `Enum` |

### `list` — Inspect database contents

```bash
dotnet run -- list
```

Shows solutions, projects, node counts, and embedding coverage.

### `models` — Manage AI model configurations

```bash
dotnet run -- models list
dotnet run -- models add summarize qwen3-coder --provider ollama --max-prompt-chars 8000
dotnet run -- models add embedding nomic-embed-text --provider ollama --dimensions 768
dotnet run -- models remove qwen3-coder
dotnet run -- models default summarize claude-haiku-4-5-20251001
```

Model configurations are stored in `~/.graphragcli/models.json`.

### Global option

All commands that interact with Neo4j accept `-d` / `--database` to specify which container to target. If only one managed container is running, it auto-detects.

```bash
dotnet run -- search "query" -d my-project
dotnet run -- summarize -d my-project
```

## Architecture

See [docs/architecture.md](docs/architecture.md) for the overview, then dive into each stage:

Pipeline stages:
- [Ingest](docs/pipeline/ingest.md) — Roslyn analysis, graph creation, post-processing
- [Summarize](docs/pipeline/summarize.md) — tiering, prompts, batch vs concurrent, retry logic
- [Embed](docs/pipeline/embed.md) — vector embeddings, centrality scores
- [Search](docs/pipeline/search.md) — hybrid retrieval, RRF, graph reranking

Reference:
- [Graph schema](docs/reference/graph-schema.md) — node types, relationships, properties
- [Incremental updates](docs/reference/incremental-updates.md) — change detection, dirty flag propagation
- [Model configuration](docs/reference/model-configuration.md) — providers, models.json, searchText strategy

## MCP integration

GraphRAG CLI databases can be queried directly from Claude Code using the Neo4j MCP server.

### Setup

```bash
brew install neo4j/homebrew-tap/neo4j-mcp
```

The `database init` and `database adopt` commands output MCP configuration JSON that you can add to `.mcp.json`:

```json
{
  "mcpServers": {
    "graphrag-cli": {
      "command": "neo4j-mcp",
      "env": {
        "NEO4J_URI": "bolt://localhost:7687",
        "NEO4J_USERNAME": "neo4j",
        "NEO4J_PASSWORD": "password123"
      }
    }
  }
}
```

This gives Claude Code access to `get-schema`, `read-cypher`, and `write-cypher` tools for querying the code graph.

### `/ask-codebase` skill

The custom Claude Code skill at `.claude/skills/ask-codebase/` auto-fires when codebase questions are asked, combining the search CLI with Neo4j MCP queries to answer questions about any indexed codebase.
