## Codebase questions

When the user asks a question about how this codebase works, use the `/ask-codebase` skill to answer it. The code intelligence graph in Neo4j has summaries, relationships, and source text for all nodes — prefer querying the graph over reading files directly.

## Documentation updates

Use `/update-docs` when the user asks to update or regenerate documentation.

## Architecture

Vertical slice architecture: `Features/` contains self-contained feature slices, `Shared/` contains cross-cutting infrastructure. Features never reference other features, only Shared.

## Build & run

```bash
dotnet build
dotnet run -- <command>
```
