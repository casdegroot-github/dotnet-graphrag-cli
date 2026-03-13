# Graph schema

## Node types

| Label | Description | Key properties |
|-------|-------------|----------------|
| `Solution` | Top-level .sln file | `fullName`, `summary`, `tier` |
| `Project` | .csproj project | `fullName`, `summary`, `tier` |
| `Namespace` | C# namespace | `fullName`, `summary`, `tier` |
| `Class` | C# class | `fullName`, `sourceText`, `summary`, `bodyHash`, `tier` |
| `Interface` | C# interface | `fullName`, `sourceText`, `summary`, `tier` |
| `Enum` | C# enum | `fullName`, `members`, `summary`, `tier` |
| `Method` | C# method | `fullName`, `returnType`, `parameters`, `sourceText`, `summary`, `bodyHash`, `tier` |

## Additional labels (added by post-processing)

| Label | Meaning |
|-------|---------|
| `Embeddable` | Node type that gets summary + vector embedding (Class, Interface, Method, Enum) |
| `EntryPoint` | DI registration method (`Add*Services`, `Configure*`) |
| `PublicApi` | Public class/interface/method |

## Relationship types

| Relationship | Direction | Meaning |
|---|---|---|
| `BELONGS_TO_NAMESPACE` | Method/Class/Interface/Enum → Namespace | Code element lives in this namespace |
| `BELONGS_TO_PROJECT` | Namespace → Project | Namespace is part of this project |
| `BELONGS_TO_SOLUTION` | Project → Solution | Project belongs to solution |
| `CONTAINS_PROJECT` | Solution → Project | Solution contains project |
| `DEFINED_BY` | Method → Class/Interface | Method is defined in this type |
| `CALLED_BY` | Method → Method | Method A is called by method B |
| `IMPLEMENTS` | Class → Interface | Class implements interface |
| `IMPLEMENTS_METHOD` | Method → Method | Method implements an interface method |
| `EXTENDS` | Class → Class | Class extends base class |
| `REFERENCES` | Class/Method → Class/Interface | Code references this type |

## Node properties

### Common properties (all node types)

| Property | Type | Description |
|----------|------|-------------|
| `fullName` | string | Fully qualified name (e.g. `GraphRagCli.Features.Search.SearchService`) |
| `name` | string | Short name (e.g. `SearchService`) |
| `summary` | string | LLM-generated summary |
| `searchText` | string | Keyword-dense text optimized for vector search |
| `tags` | string[] | Category tags (e.g. `DATABASE`, `API`, `PIPELINE`) |
| `tier` | int | Hierarchical depth for summarization ordering |
| `embedding` | float[] | Vector embedding for semantic search |
| `embeddingHash` | string | Hash of content used to generate the embedding |
| `lastIngestedAt` | datetime | Timestamp of last ingestion run |
| `needsSummary` | boolean | Whether this node needs (re-)summarization |
| `needsEmbedding` | boolean | Whether this node needs (re-)embedding |
| `bodyHash` | string | SHA-256 hash of source text for change detection |
| `pageRank` | float | PageRank centrality score |
| `degree` | int | Node degree (number of relationships) |

### Method-specific

| Property | Type | Description |
|----------|------|-------------|
| `returnType` | string | Method return type |
| `parameters` | string | Method parameter list |
| `sourceText` | string | Full method source code |

### Enum-specific

| Property | Type | Description |
|----------|------|-------------|
| `members` | string | Comma-separated enum members |