Answer a question about the indexed codebase using semantic search and the Neo4j code graph.

## Question
$ARGUMENTS

## Tools

**Search CLI** (run 3-5 queries in parallel):
```
dotnet run -- search "$query" --top 5 -d $database
```

**Neo4j MCP** (use `graphrag-cli:read-cypher` tool):
- Node summary: `MATCH (n) WHERE n.fullName = '...' RETURN n.summary, n.searchText, labels(n), n.tier`
- Relationships: `MATCH (n)-[r]-(m) WHERE n.fullName = '...' RETURN type(r), m.fullName, m.summary LIMIT 20`
- Namespace overview: `MATCH (ns:Namespace) WHERE ns.fullName CONTAINS '...' RETURN ns.fullName, ns.summary`
- Multi-hop: `MATCH (n)-[*1..3]-(m) WHERE n.fullName = '...' RETURN DISTINCT labels(m), m.fullName, m.summary LIMIT 30`

## Process

1. **Expand the query** — generate 3-5 search phrasings covering: user's terms, .NET/C# synonyms, expected class/interface names, and domain terms.

2. **Search in parallel** — run all expanded queries simultaneously via the search CLI. Use `--mode Hybrid` for most queries.

3. **Follow the graph** — don't stop at direct results. For each key result:
   - Get its relationships: callers, callees, parent class/namespace, interfaces it implements
   - Follow containment: Method → Class → Namespace → Project
   - Check sibling types in the same namespace
   - Use multi-hop Cypher for deeper exploration

4. **Iterate if incomplete** — if findings don't fully answer the question, formulate refined queries based on what you learned and search again.

5. **Synthesize** — answer with specific type/method names, API surfaces, and architecture patterns. Reference graph relationships.

## Rules
- Every claim must trace to a search result or graph query
- Use full type names (e.g. `GraphRagCli.Features.Search.SearchService`)
- If the graph lacks information, say so — do not speculate
- Prefer graph traversal over assumptions about code structure