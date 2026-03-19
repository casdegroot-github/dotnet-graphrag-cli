> *Generated from the code intelligence graph.*

# CLI Reference

Complete reference for every GraphRagCli command, flag, and option.

**Navigation:** [Overview](index.md) | [Design Decisions](design-decisions.md) | [Pipeline](pipeline/) | [Platform](platform/) | **CLI Reference**

---

## Global Options

The following option is shared across most pipeline and utility commands via `DatabaseOption`:

| Flag | Type | Default | Description |
|------|------|---------|-------------|
| `--database`, `-d` | `string?` | Auto-detects if only one container is running | Database container name |

---

## Database Management

### `database init`

Spin up a new Neo4j Docker container for code graph storage.

```
graphragcli database init [--name <name>] [--port <port>] [--password <password>]
```

| Flag | Type | Default | Description |
|------|------|---------|-------------|
| `--name` | `string?` | Derived from current directory | Database name |
| `--port` | `int?` | Auto-find free port starting at 7687 | Bolt port |
| `--password` | `string` | `password123` | Neo4j password |

**Exit codes:** `0` on success or if already running; `1` on container creation failure.

**What it does:** Creates and starts a Neo4j Docker container, then outputs an MCP JSON configuration template (containing Bolt port, password, URI, database name) for integration into your `.mcp.json` file. Prints next-step CLI commands for ingestion and embedding. On failure, prints the `docker logs` command for debugging. See [Platform](platform/) for infrastructure details.

**Examples:**

```bash
# Initialize with defaults (name from current directory, auto-port)
graphragcli database init

# Specify name and port
graphragcli database init --name my-project --port 7688

# Custom password
graphragcli database init --name my-project --password s3cret
```

**Example output:**

```
Initializing Neo4j container
Neo4j is ready!
Add this to your project's .mcp.json:

    {
      "mcpServers": {
        "my-project": {
          "command": "neo4j-mcp",
          "env": {
            "NEO4J_URI": "bolt://localhost:7687",
            "NEO4J_USERNAME": "neo4j",
            "NEO4J_PASSWORD": "password123",
            "NEO4J_DATABASE": "neo4j",
            "NEO4J_TRANSPORT_MODE": "stdio"
          }
        }
      }
    }

Next steps:
  dotnet run -- ingest -d my-project <path-to-solution-or-project>
  dotnet run -- embed -d my-project
```

If the container already exists:

```
Initializing Neo4j container
Database 'my-project' is already running on bolt://localhost:7687
Add this to your project's .mcp.json:
    ...
```

---

### `database list`

List all GraphRagCli-managed Neo4j containers.

```
graphragcli database list
```

No options. Displays a formatted table with Name (30 chars), Status (25 chars), and Bolt port columns. If no containers exist, prints guidance to create one with `database init`.

**Exit codes:** `0` on success.

**Examples:**

```bash
graphragcli database list
```

**Example output:**

```
Name                           Status                    Bolt port
---------------------------------------------------------------------------
my-project                     running (Up 3 hours)      7687
other-project                  exited (Exited 2 days)
```

When no containers exist:

```
No GraphRagCli databases found. Create one with: database init --name <name>
```

---

### `database adopt`

Adopt an existing Docker container into the GraphRagCli group.

```
graphragcli database adopt <container>
```

| Argument | Type | Required | Description |
|----------|------|----------|-------------|
| `container` | `string` | Yes | Name of the existing Docker container to adopt |

**Exit codes:** `0` on success; `1` if container not found or already managed.

**What it does:** Verifies the container exists and is not already managed, persists it to the adoption registry, then outputs an MCP JSON configuration template with Neo4j connection details (Bolt port, password, URI, username, database name). See [Platform](platform/) for container lifecycle details.

**Examples:**

```bash
graphragcli database adopt my-neo4j-container
```

**Example output:**

```
Container 'my-neo4j-container' adopted successfully.
Add this to your project's .mcp.json:

    {
      "mcpServers": {
        "my-neo4j-container": {
          "command": "neo4j-mcp",
          "env": {
            "NEO4J_URI": "bolt://localhost:7687",
            "NEO4J_USERNAME": "neo4j",
            "NEO4J_PASSWORD": "password123",
            "NEO4J_DATABASE": "neo4j",
            "NEO4J_TRANSPORT_MODE": "stdio"
          }
        }
      }
    }
```

If the container is not found:

```
Container 'bad-name' not found.
```

---

## Pipeline Stages

### `ingest`

Analyze a C# solution and ingest the code graph into Neo4j.

```
graphragcli ingest <solution-path> [--skip-tests] [--skip-samples] [-d <database>]
```

| Argument / Flag | Type | Default | Description |
|-----------------|------|---------|-------------|
| `solution-path` | `string` | *(required)* | Path to solution file or directory |
| `--skip-tests` | `bool` | `false` | Skip projects containing "Test" or "Tests" |
| `--skip-samples` | `bool` | `false` | Skip projects containing "Sample", "Example", or "Playground" |
| `-d`, `--database` | `string?` | Auto-detect | Database container name |

**Exit codes:** `0` on success; `1` on failure (missing solution file or Neo4j connection error).

**What it does:** Resolves the target solution/project via `SolutionResolver`, analyzes code with Roslyn, and bulk-loads entities (namespaces, classes, interfaces, methods, enums, call/reference relationships) into Neo4j. Performs post-processing: reconciles stale nodes (transferring enriched metadata via bodyHash matching), labels entry points and public API surfaces, computes hierarchical tiers via topological sort, and resolves NuGet dependencies. Prints per-project symbol counts, reconciliation statistics, entry point linking counts, and public API surface breakdown. See [Pipeline &mdash; Ingest](pipeline/) for details.

**Examples:**

```bash
# Ingest a solution
graphragcli ingest ./MySolution.sln

# Ingest, skipping test projects
graphragcli ingest ./MySolution.sln --skip-tests

# Ingest into a specific database container
graphragcli ingest ./MySolution.sln -d my-project --skip-tests --skip-samples
```

**Example output:**

```
GraphRagCli - Ingest
  Solution:     /home/user/src/MySolution.sln
  Database:     my-project
  Skip tests:   True
  Skip samples: False

=== Ingestion Results ===

  MyProject.Core:
    Nodes: 5 namespaces, 42 classes, 8 interfaces, 187 methods, 3 enums
    Edges: 312 calls, 94 references

  MyProject.Api:
    Nodes: 3 namespaces, 18 classes, 4 interfaces, 76 methods, 1 enums
    Edges: 145 calls, 38 references

--- Reconcile ---
Cleaned up 4 stale nodes and 12 stale edges.
Transferred 2 embeddings from renamed/moved nodes.

Solution node 'MySolution' created with 2 projects.
Linked 23 interface method implementations.
Labeled 14 entry points.

Public API surface (3 packages):
  12 public classes
  4 public interfaces
  47 public methods
  Total PublicApi: 63

Done! Open Neo4j Browser at http://localhost:7474 to explore the graph.
```

When the graph is already up to date:

```
--- Reconcile ---
Graph is up to date — no stale nodes or edges.
```

---

### `summarize`

Generate LLM summaries for code graph nodes.

```
graphragcli summarize [--model <model>] [--force] [--batch] [--sample] [--tier <n>...] [--id <elementId>] [--list-tiers] [--prompt <text>] [-d <database>]
```

| Flag | Type | Default | Description |
|------|------|---------|-------------|
| `--model` | `string?` | From `models.json` | Summary model name |
| `--force` | `bool` | `false` | Re-summarize all nodes, not just changed |
| `--batch` | `bool` | `false` | Use Claude Batch API (50% cheaper, async processing) |
| `--sample` | `bool` | `false` | Test with 1 node per type |
| `--tier` | `int[]?` | All tiers | Only process specific tiers (can specify multiple) |
| `--id` | `string?` | *(none)* | Only process a specific node by its elementId |
| `--list-tiers` | `bool` | `false` | List tier breakdown and exit |
| `--prompt` | `string?` | *(none)* | Custom prompt instruction (overrides default) |
| `-d`, `--database` | `string?` | Auto-detect | Database container name |

**Validation:** If `--batch` is set, the model provider must be Claude. Validated before execution via `SummarizeCommand`.

**Exit codes:** `0` on success; `1` on exception.

**What it does:** Orchestrates tier-by-tier AI-driven summarization of Neo4j code graph nodes. Uses map-reduce chunking for oversized nodes (splitting by line boundaries up to MaxPromptChars, summarizing chunks independently, then reducing to a final architectural overview). Builds prompts with relational context, persists summaries, search text, and semantic tags back to Neo4j in batches of 50. When batch mode is used, prints a Claude API usage report (token counts and estimated USD cost with 50% batch discount). See [Pipeline &mdash; Summarize](pipeline/) for details.

**Examples:**

```bash
# Summarize all unsummarized nodes with default model
graphragcli summarize

# Force re-summarize everything with a specific model
graphragcli summarize --model qwen3-coder --force

# Use Claude batch API for cost savings
graphragcli summarize --batch

# Test with a single sample per type
graphragcli summarize --sample

# Only process tier 1 and tier 2 nodes
graphragcli summarize --tier 1 --tier 2

# List tier breakdown without processing
graphragcli summarize --list-tiers

# Summarize a specific node
graphragcli summarize --id "4:abc123:0"

# Custom prompt instruction
graphragcli summarize --prompt "Focus on public API contracts"
```

**Example output:**

```
GraphRagCli - Summarize
  Database:   my-project
  Model:      qwen3-coder (ollama)
  Force:      False
  Concurrency:4

Using ollama for summaries (model: qwen3-coder, concurrency: 4)

=== Tier 0: 12 nodes ===
  [##############################] 12/12 (100%) | 01:23 elapsed | ETA 00:00 | MyProject.Core.Models.User

Done! Summarized 12/12 nodes in 01:23.

=== Tier 1: 38 nodes ===
  Node 'MyProject.Core.Services.BigService' is oversized (24,300 chars). Processing in Map-Reduce mode...
  [##############################] 38/38 (100%) | 04:15 elapsed | ETA 00:00 | MyProject.Api.Controllers.UserController

Done! Summarized 38/38 nodes in 04:15.

=== Tier 2: 5 nodes ===
  [##############################] 5/5 (100%) | 00:32 elapsed | ETA 00:00 | MyProject.Core

Done! Summarized 5/5 nodes in 00:32.
```

With `--batch` (Claude Batch API), a usage report is appended:

```
=== Claude API Usage Report ===
  Input tokens:   124,500
  Output tokens:  18,200
  Total tokens:   142,700
  Estimated cost: $0.4281
  Model:          claude-sonnet-4-20250514
  Mode:           batch (50% discount applied)
```

With `--list-tiers`:

```
Tier 0: 42 Classes, 8 Interfaces, 3 Enums (12 pending)
Tier 1: 187 Methods (45 pending)
Tier 2: 5 Namespaces (0 pending)
Tier 3: 2 Projects (2 pending)
```

With `--sample`, each result is printed for review:

```
--- MyProject.Core.Models.User ---
SUMMARY: Domain entity representing an authenticated user with role-based access...
SEARCH:  User domain entity, role-based access, authentication, profile management
TAGS: domain-model, authentication, rbac
```

With `--id`, a side-by-side comparison is shown:

```
================================================================================
COMPARISON FOR: MyProject.Core.Models.User (4:abc123:0)
================================================================================

--- OLD SUMMARY ---
Represents a user in the system.

--- PROMPT SENT TO LLM ---
<the full prompt text>

--- GENERATING NEW SUMMARY... ---

--- NEW SUMMARY ---
Domain entity representing an authenticated user with role-based access control...

TAGS: domain-model, authentication, rbac
================================================================================
```

---

### `embed`

Generate embeddings from existing summaries and compute centrality scores.

```
graphragcli embed [--model <model>] [--force] [--max-concurrency <n>] [-d <database>]
```

| Flag | Type | Default | Description |
|------|------|---------|-------------|
| `--model` | `string?` | From `models.json` | Embedding model name |
| `--force` | `bool` | `false` | Re-embed all nodes, not just those needing embedding |
| `--max-concurrency` | `int?` | `4` | Max concurrent embedding API calls |
| `-d`, `--database` | `string?` | Auto-detect | Database container name |

**Exit codes:** `0` on success; `1` on exception.

**What it does:** Validates vector index schema, fetches nodes with summaries, generates embeddings concurrently via the configured embedding model, persists vectors to Neo4j, stores model metadata, and computes PageRank and in-degree centrality scores. Displays configuration details (database, model name, dimensions, force flag) before execution. See [Pipeline &mdash; Embed](pipeline/) for details.

**Examples:**

```bash
# Embed with default model
graphragcli embed

# Force re-embed all nodes
graphragcli embed --force

# Use a specific model with higher concurrency
graphragcli embed --model nomic-embed-text --max-concurrency 8

# Target a specific database
graphragcli embed -d my-project
```

**Example output:**

```
GraphRagCli - Embed
  Database:   my-project
  Model:      nomic-embed-text
  Dimensions: 768
  Force:      False

  [##############################] 245/245 (100%) | 02:14 elapsed | ETA 00:00 | MyProject.Core.Services.AuthService

Done! Embedded 245/245 nodes in 02:14.
```

When no nodes need embedding:

```
GraphRagCli - Embed
  Database:   my-project
  Model:      nomic-embed-text
  Dimensions: 768
  Force:      False

No nodes to embed.
```

---

## Search

### `search`

Search the code graph using semantic and graph-augmented queries.

```
graphragcli search <query> [--top <n>] [--type <type>] [--mode <mode>] [-d <database>]
```

| Argument / Flag | Type | Default | Description |
|-----------------|------|---------|-------------|
| `query` | `string` | *(required)* | Search query |
| `--top` | `int` | `10` | Number of results |
| `--type` | `string?` | *(none)* | Filter by type: `Class`, `Interface`, `Method`, `Enum` |
| `--mode` | `SearchMode` | `Hybrid` | Search mode: `Hybrid` (fulltext+vector) or `Vector` |
| `-d`, `--database` | `string?` | Auto-detect | Database container name |

**Exit codes:** `0` on success; `1` on exception.

**What it does:** Embeds the query using the configured embedding model, retrieves candidates via vector or hybrid search with graph-based reranking using PageRank centrality, then formats results to console. Displays method signatures with return types and parameters, truncated summaries (max 60 chars), search text snippets, and up to 3 neighboring graph nodes with relationships. See [Pipeline &mdash; Search](pipeline/) for details.

**Examples:**

```bash
# Basic semantic search
graphragcli search "dependency injection setup"

# Search for classes only, top 5 results
graphragcli search "repository pattern" --type Class --top 5

# Pure vector search (no fulltext component)
graphragcli search "error handling" --mode Vector

# Search a specific database
graphragcli search "authentication flow" -d my-project
```

**Example output:**

```
Searching for: "dependency injection setup" [Hybrid]

Score    Type         Name                                               Summary
------------------------------------------------------------------------------------------------------------------------
0.8912  Class        MyProject.Core.DI.ServiceRegistration              [T3 | P4.21] Registers all core services into...
         search: dependency injection, service registration, DI container, IServiceCollection extensions
           └─ DEFINED_BY     Program                                    Entry point bootstrapping the DI container...
           └─ REFERENCES     IUserRepository                            Repository interface for user persistence...
           └─ REFERENCES     AuthService                                Authentication service with JWT token gen...
0.8534  Method       MyProject.Api.Startup.ConfigureServices            [T2 | P2.87] Configures DI services for the ...
         sig: void ConfigureServices(IServiceCollection services)
         search: ASP.NET service configuration, middleware pipeline, DI registration
           └─ CALLED_BY      Program.Main                               Application entry point...
0.7201  Interface    MyProject.Core.Data.IRepository                    [T1 | P1.45] Generic repository interface fo...
         search: generic repository pattern, data access abstraction, CRUD operations

3 results returned.
```

When no results are found:

```
Searching for: "nonexistent concept" [Hybrid]
No results found.
```

---

## Model Management

### `models add`

Add a model configuration for embedding or summarization.

```
graphragcli models add <type> <name> --provider <provider> [options]
```

| Argument / Flag | Type | Default | Description |
|-----------------|------|---------|-------------|
| `type` | `string` | *(required)* | Model type: `embedding` or `summarize` |
| `name` | `string` | *(required)* | Model name (e.g., `nomic-embed-text`, `qwen3-coder`) |
| `--provider` | `string` | *(required)* | AI provider (e.g., `ollama`, `claude`) |
| `--dimensions` | `int?` | *(none)* | Embedding dimensions (required for embedding models) |
| `--document-prefix` | `string?` | *(none)* | Document prefix for embedding |
| `--query-prefix` | `string?` | *(none)* | Query prefix for embedding |
| `--max-prompt-chars` | `int?` | *(none)* | Max prompt characters (required for summarize models) |
| `--concurrency` | `int` | `1` | Max concurrency for summarization |

**Exit codes:** `0` on success; `1` on validation failure (missing required type-specific parameters).

**What it does:** Validates type-specific required parameters (dimensions for embedding, max-prompt-chars for summarize), constructs the model configuration, and persists it to `models.json`.

**Examples:**

```bash
# Add an embedding model
graphragcli models add embedding nomic-embed-text --provider ollama --dimensions 768

# Add an embedding model with prefixes
graphragcli models add embedding nomic-embed-text --provider ollama --dimensions 768 \
  --document-prefix "search_document: " --query-prefix "search_query: "

# Add a summarization model
graphragcli models add summarize qwen3-coder --provider ollama --max-prompt-chars 8000

# Add a Claude summarization model with concurrency
graphragcli models add summarize claude-sonnet --provider claude --max-prompt-chars 16000 --concurrency 4
```

**Example output:**

```
Added embedding model 'nomic-embed-text'
```

Validation errors:

```
Error: --dimensions is required for embedding models
```

```
Error: --max-prompt-chars is required for summarize models
```

```
Error: Type must be 'embedding' or 'summarize'
```

---

### `models list`

List all configured models.

```
graphragcli models list
```

No options. Displays embedding and summarization models with provider, dimensions (for embedding), max prompt chars and concurrency (for summarize), and marks default models with an asterisk (`*`).

**Exit codes:** `0` on success.

**Example output:**

```
Embedding models:
  * nomic-embed-text                ollama    768 dims  (default)
    text-embedding-3-small          openai   1536 dims

Summarize models:
  * qwen3-coder                     ollama    8000 chars  concurrency: 4  (default)
    claude-sonnet                   claude   16000 chars  concurrency: 1
```

---

### `models remove`

Remove a model configuration.

```
graphragcli models remove <name>
```

| Argument | Type | Required | Description |
|----------|------|----------|-------------|
| `name` | `string` | Yes | Model name to remove |

**Exit codes:** `0` on success; `1` if model is currently set as a default or not found.

**What it does:** Removes the named model from either the Embedding or Summarize collection in `models.json`. Refuses to remove a model that is currently configured as a default -- change the default first.

**Examples:**

```bash
graphragcli models remove old-embedding-model
```

**Example output:**

```
Removed model 'old-embedding-model'
```

Error cases:

```
Error: Cannot remove 'nomic-embed-text' — it is the current default. Change the default first.
```

```
Error: Model 'nonexistent' not found
```

---

### `models default`

Set the default model for a given type.

```
graphragcli models default <type> <name>
```

| Argument | Type | Required | Description |
|----------|------|----------|-------------|
| `type` | `string` | Yes | Model type: `embedding` or `summarize` |
| `name` | `string` | Yes | Model name |

**Exit codes:** `0` on success; `1` if model type is invalid or the specified model does not exist.

**What it does:** Updates the default model setting for the specified type (Embedding or Summarize) in `models.json`.

**Examples:**

```bash
# Set default embedding model
graphragcli models default embedding nomic-embed-text

# Set default summarize model
graphragcli models default summarize qwen3-coder
```

**Example output:**

```
Default embedding model set to 'nomic-embed-text'
```

Error cases:

```
Error: Embedding model 'nonexistent' not found. Add it first.
```

```
Error: Type must be 'embedding' or 'summarize'
```

---

## Utilities

### `list`

Show database contents: projects, node counts, and embedding coverage.

```
graphragcli list [-d <database>]
```

| Flag | Type | Default | Description |
|------|------|---------|-------------|
| `-d`, `--database` | `string?` | Auto-detect | Database container name |

**Exit codes:** `0` on success; `1` on exception.

**What it does:** Queries Neo4j for database intelligence metrics and displays: database label, solutions list, node counts by label (sorted descending with totals), embedding coverage ratio, and projects with member counts and summaries (truncated to 120 chars).

**Examples:**

```bash
# List contents of auto-detected database
graphragcli list

# List contents of a specific database
graphragcli list -d my-project
```

**Example output:**

```
=== Database: my-project ===

Solutions:
  MySolution
    Multi-project C# solution for a web API with core domain logic...

Node counts:
  Method         187
  Class           42
  Interface        8
  Namespace        5
  Enum             3
  Total          245

Embedding coverage:
  Embedded:   245 / 245

Projects (2):

  MyProject.Core (156 members)
    Core domain library containing entities, repository interfaces, and business logic services for...

  MyProject.Api (89 members)
    ASP.NET Web API project with controllers, middleware, and DI configuration for...
```

---

## Typical Workflow

A complete pipeline run follows this sequence:

```bash
# 1. Create a database
graphragcli database init --name my-project

# 2. Ingest your codebase
graphragcli ingest ./MySolution.sln -d my-project --skip-tests

# 3. Generate summaries
graphragcli summarize -d my-project

# 4. Generate embeddings
graphragcli embed -d my-project

# 5. Search the graph
graphragcli search "how does authentication work" -d my-project

# 6. Inspect database contents
graphragcli list -d my-project
```

---

## Exit Code Summary

| Code | Meaning |
|------|---------|
| `0` | Success |
| `1` | Failure (missing input, connection error, validation error, or runtime exception) |
