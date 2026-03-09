Answer a question about the Chabis.DataPipeline codebase using semantic search and the Neo4j code graph.

## Question
$ARGUMENTS

## Tools

**Search CLI** (run 3-5 queries in parallel):
```
dotnet run --project Chabis.Application.Playground.GraphRag-2 -- search --claude "query" --top 5
```
Working directory: /Users/casdegroot/Documents/repositories/Chabis.Application/src/Playground/Chabis.Application.Playground.GraphRag

**Neo4j** (use `neo4j:read-cypher` MCP tool):
- Node summaries: `MATCH (n) WHERE n.fullName = '...' RETURN n.claude_summary`
- Relationships: `MATCH (n)-[r]-(m) WHERE n.fullName = '...' RETURN type(r), m.name, m.claude_summary`
- Namespace overview: `MATCH (ns:NamespaceSummary) WHERE ns.name CONTAINS '...' RETURN ns.claude_summary`

**Sample project** (read real usage patterns):
```
/Users/casdegroot/Documents/repositories/Chabis.DataPipeline/sample/Chabis.DataPipeline.TestPipeline/
```
When the question involves "how to set up" or "how to implement", read relevant files from the sample project to see real conventions and patterns.

## Process

1. **Clarify ambiguity** — if the question involves implementation choices (e.g. transport layer: Kafka vs MongoDB, consumer type: simple vs branching, single vs multi-database), ask before proceeding.

2. **Expand the query** — generate 3-5 search phrasings covering: user's terms, .NET/C# synonyms, Chabis domain terms (e.g. "read model" → "target document aggregator"), and expected class/interface names.

3. **Search in parallel** — run the expanded queries. Use `--mode Hybrid` for most queries (auto-routes between name-weighted and semantic-weighted). Use `--mode Vector` if you want pure semantic similarity.

4. **Follow the graph exhaustively** — don't stop at direct results. For each key type:
   - Get ALL interfaces it can implement — search for sibling interfaces in the same namespace, not just the ones you already found
   - Follow the "what does this need?" chain: if a type uses `InputChangeAnalyzerStrategy.Hash`, ask "what interfaces must my document implement for Hash to work?" and search for `IAmHashedDocument`, `[HashIgnore]`, etc.
   - For base classes, find ALL alternative base classes in the same namespace (e.g. both `InputBatchConsumerBase` and `BranchInputBatchConsumerBase` exist — explain when to use each)
   - For registration methods, find ALL related registration methods (e.g. `AddMongoDbCollection` + `AddChangesMongoDbCollection` + `DeployMongoDbCollectionAsync` are all needed)
   - Use multi-hop Cypher: `MATCH (n)-[*1..3]-(m) WHERE n.fullName = '...' RETURN DISTINCT labels(m), m.name, m.claude_summary`

5. **Check the sample project** — for "how to" questions, read the relevant sample files to verify patterns:
   - Document models: look at partial class conventions, `[HashIgnore]`, `IAmHashedDocument`, `IHaveUpdateTimestampsDocument`
   - Consumers: check `InputBatchConsumerBase` vs `BranchInputBatchConsumerBase` usage, `[KafkaBatchConsumer]` attribute, mapper classes
   - Aggregators: check `IAggregateAndPropagateTargetDocument` + `ChangeHandler` batch-prefetch pattern, `IProduceFullSyncTargetChanges`, documentation attributes
   - DI setup: check `ServiceProviderConfig` classes, multi-database registration, host roles

6. **Iterate if incomplete** — if findings don't fully answer the question, formulate refined queries based on what you learned and search again. Err on the side of exploring too much rather than too little.

7. **Synthesize** — answer with specific type/method names, API surfaces, and architecture patterns. Reference graph relationships. Only state what the graph confirms.

## Rules
- Every claim must trace to a search result or graph query
- Use full type names (e.g. `Chabis.DataPipeline.Abstractions.IDependOn<T>`)
- If the graph lacks information, say so — do not speculate
- When showing implementation plans, match the conventions from the sample project (partial classes, mappers, ChangeHandler pattern, documentation attributes)
