Regenerate the `docs/` directory from scratch using the code intelligence graph in Neo4j. Delete all existing docs first — they are always fully derivable from the graph.

Use the Neo4j MCP server (`read-cypher`) as your primary source. Use the project's search CLI if available for semantic questions.

## Approach: Top-down graph traversal

The graph is a hierarchy with tiers. Start at the top (solution/project) and walk down. Each level of the hierarchy maps to a level of documentation detail.

### Step 1: Read the top

Query the highest-tier nodes (solution, project). Their summaries give you the system overview — what it does, what technologies it uses, how the pieces fit together.

```
MATCH (n) WHERE n:Solution OR n:Project
RETURN n.fullName, n.summary, n.tier, labels(n)
ORDER BY n.tier DESC
```

### Step 2: Walk down to namespaces/modules

Query the next level — namespaces, modules, or packages. These reveal the major subsystems and features. Their summaries describe what each area does.

```
MATCH (n:Namespace)
RETURN n.fullName, n.summary, n.tier
ORDER BY n.tier DESC
```

Group related namespaces. This grouping determines your doc structure — each logical group becomes a page or section.

### Step 3: Walk into types and methods

For each subsystem, query its children — classes, interfaces, enums, methods. Their summaries explain how things actually work.

```
MATCH (parent:Namespace)--(child)
WHERE parent.fullName = '...'
RETURN child.fullName, child.summary, labels(child)
```

Follow relationships to understand data flow:
```
MATCH (n)-[r]-(m) WHERE n.fullName = '...'
RETURN type(r), m.fullName, m.summary
```

### Step 4: Check cross-cutting concerns

Query for patterns that span multiple subsystems:
- Entry points / DI registrations
- Shared utilities and base classes
- External integrations
- Configuration types

These become reference docs.

## Writing

Based on the traversal, generate docs. Let the graph's hierarchy determine the folder structure — don't use a fixed template. A small project might need 3 pages, a large one 15.

**Overview page** (from top-tier nodes):
- What the system does (top-level summary)
- Mermaid diagram of the main data/control flow
- Project structure (from namespace hierarchy)
- Links to all deeper docs

**Subsystem pages** (from mid-tier namespaces):
- What this subsystem does (namespace summary)
- Mermaid diagram (sequenceDiagram for linear flows, flowchart for branching)
- Walkthrough referencing actual class/method names
- Non-obvious behavior from summaries
- Key components table: `| Component | Role |`

**Reference pages** (from cross-cutting queries):
- Tabular format for schemas, properties, config
- No overlap with subsystem pages — link instead

## Rules

- Every claim traces to a graph node or relationship. Don't invent.
- Never repeat across pages. One canonical location, link everywhere else.
- Mermaid for all diagrams. One concept per diagram.
- Tables over prose for structured data.
- Use the graph's own language from summaries — don't rephrase it worse.
- Every page starts with: `> *Generated from the code intelligence graph.*`
- Name docs after what they describe, not code structure.
- After writing, verify cross-references and update README if it links to docs.