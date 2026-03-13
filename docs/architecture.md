# Architecture

> Auto-generated from the GraphRAG CLI code intelligence graph.

## Overview

GraphRagCli is a C# CLI tool that transforms codebases into Neo4j knowledge graphs for semantic search and AI-powered analysis. It combines code ingestion, AI summarization, and graph database integration to enable retrieval-augmented generation and intelligent code navigation.

## Project structure

The codebase follows a vertical slice architecture: each feature is self-contained under `Features/`, and cross-cutting concerns live in `Shared/`.

```
GraphRagCli/
├── Features/
│   ├── Database/       # Neo4j container lifecycle
│   ├── Embed/          # Vector embeddings + centrality
│   ├── Ingest/         # Roslyn analysis → graph
│   │   ├── Analysis/   # Syntax tree parsing
│   │   └── GraphDb/    # Neo4j ingestion + post-processing
│   ├── List/           # Database inspection
│   ├── Models/         # AI model configuration CLI
│   ├── Search/         # Hybrid search (vector + fulltext)
│   └── Summarize/      # LLM summarization pipeline
│       ├── Prompts/    # Prompt engineering
│       └── Summarizers/# Concurrent + batch summarizers
├── Shared/
│   ├── Ai/             # AI provider abstraction (Ollama, Claude)
│   ├── Docker/         # Container orchestration
│   ├── GraphDb/        # Neo4j driver factory
│   ├── Options/        # Shared CLI options
│   └── Progress/       # Progress bar rendering
└── Program.cs          # DI setup + CLI entry point
```

## Data flow

```
C# Solution
    │
    ▼
┌─────────┐     ┌──────────────┐     ┌───────────┐
│  Ingest  │────▶│   Neo4j DB   │◀────│ Summarize │
│ (Roslyn) │     │  (nodes +    │     │   (LLM)   │
└─────────┘     │  edges)      │     └───────────┘
                │              │           │
                │              │     ┌─────▼─────┐
                │              │◀────│   Embed   │
                │              │     │ (vectors) │
                └──────┬───────┘     └───────────┘
                       │
                 ┌─────▼─────┐
                 │  Search   │
                 │ (hybrid)  │
                 └───────────┘
```

1. **Ingest**: Roslyn parses C# → nodes + edges in Neo4j
2. **Summarize**: LLM generates summaries tier-by-tier, bottom-up
3. **Embed**: Vector embeddings + PageRank centrality
4. **Search**: Hybrid retrieval → graph expansion → reranking

## Deep dives

- [Graph schema](graph-schema.md) — node types, relationships, labels
- [Tiering & summarization](tiering.md) — how tiers work, hierarchical summarization
- [Search pipeline](search-pipeline.md) — hybrid search, RRF, graph reranking
- [Incremental updates](incremental-updates.md) — change detection, propagation, body hash transfer
- [Features](features.md) — detailed breakdown of each feature slice