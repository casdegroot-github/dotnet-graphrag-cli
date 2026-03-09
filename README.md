# GraphRAG CLI

A .NET CLI tool that builds a code intelligence graph from C# solutions using Roslyn, Neo4j, and LLM-powered summarization. Supports semantic search, graph traversal, and integration with Claude Code via MCP.

## Prerequisites

- .NET 10 SDK
- Docker (for Neo4j)
- Ollama (for local embeddings + optional local summarization)
- Anthropic API key (optional, for Claude summarization)

## 1. Start Neo4j

```bash
docker compose up -d
```

This starts Neo4j 5.15 with APOC and Graph Data Science plugins.

- Browser: http://localhost:7474
- Bolt: bolt://localhost:7687
- Credentials: `neo4j` / `password`

## 2. Set up Ollama

Ollama is **required** — it runs the embedding model for all search operations, regardless of which summarization provider you use.

```bash
# Install (macOS)
brew install ollama

# Start the server
ollama serve

# Pull the embedding model (required)
ollama pull snowflake-arctic-embed2

# Pull the summarization model (optional, only if using Ollama for summaries)
ollama pull qwen2.5-coder:7b
```

Default endpoint: `http://localhost:11434`

## 3. Set up Claude (optional)

If using Claude for summarization instead of Ollama:

```bash
export ANTHROPIC_API_KEY=sk-ant-...
```

Claude produces better summaries but costs ~$0.40/1M input tokens (Batch API with 50% discount).

## 4. Build the CLI

```bash
dotnet build
```

## 5. Ingest a codebase

```bash
# Basic — point at a solution
dotnet run -- ingest /path/to/MySolution.sln

# Skip test and sample projects
dotnet run -- ingest /path/to/MySolution.sln --skip-tests --skip-samples

# Use a .slnf to mark which projects are NuGet packages (labels public API surface)
dotnet run -- ingest /path/to/MySolution.sln --nuget-slnf /path/to/nuget-projects.slnf
```

This parses all C# code with Roslyn and creates the graph in Neo4j (nodes + relationships).

## 6. Generate summaries and embeddings

```bash
# With Ollama (free, ~30 min for ~700 nodes)
dotnet run -- embed --provider Ollama

# With Claude (faster, ~3 min, costs money)
dotnet run -- embed --provider Claude

# With Claude Batch API (50% cheaper, async)
dotnet run -- embed --provider Claude --batch

# Only run a specific pass
dotnet run -- embed --provider Claude --pass 1   # leaf nodes only
dotnet run -- embed --provider Claude --pass 2   # contextual nodes
dotnet run -- embed --provider Claude --pass 3   # namespace summaries

# Test with a small sample first
dotnet run -- embed --provider Claude --sample
```

Incremental by default — only re-embeds changed or stale nodes. Use `--force` to re-embed everything.

## 7. Search the graph

```bash
# Hybrid search (default — fulltext + vector, auto-routed)
dotnet run -- search "how does retry work" --top 5

# Use Claude embeddings/summaries
dotnet run -- search "how does retry work" --top 5 --claude

# Vector-only search
dotnet run -- search "change detection" --mode Vector

# Filter by type
dotnet run -- search "input pipeline" --type Class
```

## 8. Configure Neo4j MCP for Claude Code

Add the Neo4j MCP server so Claude Code can query the graph directly during conversations.

Run:
```bash
claude mcp add neo4j -s user -- npx -y @anthropic-ai/mcp-remote https://neo4j.mcp.run/sse?nonce=YOUR_NONCE
```

Or add manually to `~/.claude/settings.json`:
```json
{
  "mcpServers": {
    "neo4j": {
      "command": "npx",
      "args": ["-y", "@anthropic-ai/mcp-remote", "https://neo4j.mcp.run/sse?nonce=YOUR_NONCE"]
    }
  }
}
```

To get your nonce, sign up at https://neo4j.mcp.run and connect it to your local Neo4j instance (bolt://localhost:7687, neo4j/password).

This gives Claude Code access to:
- `get-schema` — inspect the graph structure
- `read-cypher` — run arbitrary read queries
- `write-cypher` — modify the graph

## 9. Set up the `/ask-codebase` skill

The custom Claude Code command lives at `.claude/commands/ask-codebase.md`. It combines the search CLI with Neo4j MCP queries to answer questions about the indexed codebase.

Usage:
```
/ask-codebase How do I set up a basic data pipeline?
```

This triggers a multi-step process: expand the query → run parallel searches → follow the graph → synthesize an answer grounded in the code graph.

## CLI reference

| Command | Description |
|---------|-------------|
| `ingest <solution-path>` | Parse C# solution and build the graph |
| `embed` | Generate summaries + embeddings for all nodes |
| `reembed` | Re-embed specific nodes or all nodes |
| `search <query>` | Search the graph |

### Global options

| Option | Default | Description |
|--------|---------|-------------|
| `--uri` | `bolt://localhost:7687` | Neo4j connection URI |
| `--user` | `neo4j` | Neo4j username |
| `--password` | `password123` | Neo4j password |
| `--ollama-url` | `http://localhost:11434` | Ollama endpoint |

### Embed options

| Option | Default | Description |
|--------|---------|-------------|
| `--provider` | `Ollama` | `Ollama` or `Claude` |
| `--model` | auto | Model name (defaults to `qwen2.5-coder:7b` / `claude-haiku-4-5`) |
| `--force` | false | Re-embed all nodes, not just changed |
| `--batch` | false | Use Claude Batch API (50% cheaper) |
| `--pass` | all | Run only pass 1, 2, or 3 |
| `--limit` | none | Process first N nodes only |
| `--sample` | false | Test with 1 node per type |

### Search options

| Option | Default | Description |
|--------|---------|-------------|
| `--top` | 10 | Number of results |
| `--mode` | `Hybrid` | `Hybrid` or `Vector` |
| `--claude` | false | Use Claude summaries/embeddings |
| `--type` | none | Filter: `Class`, `Interface`, `Method`, `Enum` |