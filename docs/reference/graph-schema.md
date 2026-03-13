# Graph schema

> *Generated from the code intelligence graph.*

## Node types

| Label | Count | Description | Key properties |
|-------|-------|-------------|----------------|
| `Method` | 170 | C# method | `fullName`, `sourceText`, `returnType`, `parameters`, `bodyHash` |
| `Class` | 104 | C# class, struct, or record | `fullName`, `sourceText`, `bodyHash` |
| `Namespace` | 18 | C# namespace | `fullName` |
| `Interface` | 5 | C# interface | `fullName`, `sourceText` |
| `Enum` | 2 | C# enum | `fullName`, `members` |
| `Solution` | 1 | Top-level .sln file | `fullName` |
| `Project` | 1 | .csproj project | `fullName` |

## Additional labels

Applied by the [ingest](../pipeline/ingest.md) post-processor:

| Label | Count | Meaning |
|-------|-------|---------|
| `Embeddable` | 281 | Gets summary + vector embedding (Class, Interface, Method, Enum) |
| `PublicApi` | 203 | Public class, interface, or method |
| `EntryPoint` | 5 | DI registration method (`Add*Services`, `Configure*`) |

## Relationships

| Relationship | Count | Direction | Meaning |
|---|---|---|---|
| `CALLED_BY` | 173 | Method → Method | Method A is called by method B |
| `DEFINED_BY` | 170 | Method → Class/Interface | Method is defined in this type |
| `BELONGS_TO_NAMESPACE` | 111 | Type/Method → Namespace | Code element lives in this namespace |
| `REFERENCES` | 47 | Class/Method → Class/Interface | Code references this type |
| `BELONGS_TO_PROJECT` | 18 | Namespace → Project | Namespace is part of this project |
| `IMPLEMENTS_METHOD` | 9 | Method → Method | Method implements an interface method |
| `EXTENDS` | 7 | Class → Class | Class extends base class |
| `IMPLEMENTS` | 6 | Class → Interface | Class implements interface |
| `CONTAINS_PROJECT` | 1 | Solution → Project | Solution contains project |
| `BELONGS_TO_SOLUTION` | 1 | Project → Solution | Project belongs to solution |

## Common properties

Present on all node types:

| Property | Type | Description |
|----------|------|-------------|
| `fullName` | string | Fully qualified name (e.g. `GraphRagCli.Features.Search.SearchService`) |
| `name` | string | Short name (e.g. `SearchService`) |
| `summary` | string | LLM-generated summary |
| `searchText` | string | Keyword-dense text optimized for vector search |
| `tags` | string[] | Category tags (e.g. `DATABASE`, `API`, `PIPELINE`) |
| `tier` | int | Hierarchical depth for [summarization ordering](../pipeline/summarize.md#tiers) |
| `embedding` | float[] | Vector embedding for semantic search |
| `embeddingHash` | string | Hash of content used to generate the embedding |
| `lastIngestedAt` | datetime | Timestamp of last ingestion run |
| `needsSummary` | boolean | Node needs (re-)summarization |
| `needsEmbedding` | boolean | Node needs (re-)embedding |
| `bodyHash` | string | SHA-256 of source text for [change detection](incremental-updates.md) |
| `pageRank` | float | PageRank centrality score |
| `degree` | int | Node degree (number of relationships) |

## Method-specific properties

| Property | Type | Description |
|----------|------|-------------|
| `returnType` | string | Method return type |
| `parameters` | string | Method parameter list |
| `sourceText` | string | Full method source code |

## Enum-specific properties

| Property | Type | Description |
|----------|------|-------------|
| `members` | string | Comma-separated enum members |
