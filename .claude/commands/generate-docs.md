Generate documentation from a code intelligence graph in Neo4j.

**Arguments:** `$ARGUMENTS`
Parse from arguments: database name (required), output folder (default: `docs/{solutionName}/`), audience context (optional).

---

## Setup

1. Delete the output folder if it exists — docs are always fully regenerable.
2. Query `get-schema` on the target database's MCP server to discover the graph structure.
3. **Documentation interview** — Use `AskUserQuestion` to understand what kind of docs to generate. Adapt questions to what the graph reveals, but cover:
   - **What kind of documentation?** — API reference, architecture overview, onboarding guide, integration guide, troubleshooting, or a mix?
   - **Who are the readers?** — team members, external consumers, new hires, etc.
   - **What should readers be able to do after reading?** — understand the system, integrate with it, contribute to it?
   - **Areas to emphasize or skip?** — focus areas, legacy to call out, things to ignore
   - **Depth preference?** — high-level overview only, or down to method-level detail?

   Pick 3-4 relevant questions, don't ask all. Store answers as `docContext` — this shapes every decision that follows.
4. **Community detection as a hint** — Run Leiden community detection (via GDS) to get a sense of natural groupings. Project the relevant node labels and relationship types as undirected, run `gds.leiden.stream`, then drop the projection. Use the resulting communities as a *signal* — not the final answer. Communities that are too large should be split; singletons folded into related groups.
5. **Define initial logical groups** — Query only project names and summaries. Using community hints, naming patterns, and `docContext`, define logical groups based on **what makes sense for the requested documentation type** — not code structure. An API reference groups by endpoint/service; an architecture overview groups by subsystem; an onboarding guide groups by workflow. Subfolders are fine when they aid navigation. The primary goal is that the resulting docs are **easy to read and navigate for the target audience**. Create the initial folder structure with placeholder pages. Present it to the user via `AskUserQuestion`, iterate until approved.

---

## Launch doc-writer subagents

After setup is complete, launch one subagent per top-level logical group. Each subagent is autonomous and may launch its own subagents.

**CRITICAL: Every subagent MUST receive the full `doc-writer-agent` instructions.** Read the file `.claude/agents/doc-writer-agent.md` and include its **entire contents verbatim** at the start of each subagent prompt. Then append the following context block:

```
---
## Your assignment

**Scope:** {which projects/namespaces/types this agent covers}
**Output folder:** {absolute path to this agent's output folder}
**Breadcrumbs:** {where this agent sits in the doc tree, e.g. "You are writing the 'Ingest Pipeline' section, part of 'Pipeline', within the GraphRagCli docs"}
**Full docs TOC:** {file names + titles for all pages, for cross-linking}
**docContext:** {the interview answers from setup}
**MCP server:** {mcp server name} on the {database} database
**Database name:** {database}
```

Run subagents in the background. When all complete, extract their written files (subagents may have permission issues — if so, extract content from their transcripts and write files yourself).

---

## Final pass

Once all subagents have completed and all files are written:

1. **Load the writing rules.** Read `.claude/agents/doc-writer-agent.md` and follow its Writing Rules, Querying Principles, and diagram guidelines when writing the root pages below. These pages must meet the same quality bar as the subagent-written pages.
2. **Read all generated child pages** to understand what was actually produced.
3. **Query the graph** for root-level data (node counts, relationship counts, label counts, tier distribution, top PageRank nodes, etc.) to ground the overview pages in real data — not summaries of summaries.
4. **Write `index.md`** — project overview, how-it-works diagram, graph statistics, quick start, architecture diagram, documentation nav table. Every claim must trace to graph data or a child page.
5. **Write `design-decisions.md`** (or equivalent) — cross-cutting architectural choices grounded in graph evidence (relationship patterns, tier distribution, interface/implementation structure, DI registration edges, etc.).
6. Add "See also" cross-links where related topics exist on other pages.
7. Verify no broken relative links (use a script to check all `[text](path.md)` links resolve).
