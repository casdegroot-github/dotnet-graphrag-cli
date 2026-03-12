namespace GraphRagCli.Features.Summarize.Services;

public class AggregationPromptBuilder : IAggregationPromptBuilder
{
    public List<EmbeddableNode> BuildNamespaceNodes(List<AggregationData> data, ProviderConfig config)
    {
        return data.Select(ns =>
        {
            var truncatedMembers = ns.ChildSummaries.Take(config.MaxNamespaceMembers).ToList();
            var membersText = string.Join("\n- ", truncatedMembers);
            var prompt = $"""
                Summarize namespace {ns.FullName} for a code intelligence graph.

                Given these components and their summaries, explain:
                - What business capability does this namespace provide?
                - What is the key data flow or processing pipeline?
                - How do the components collaborate to deliver that capability?

                Rules:
                - Lead with the business purpose, not "This namespace..."
                - Describe behavior and data flow, not list classes
                - 3-5 sentences max

                Components:
                - {membersText}
                """;
            return new EmbeddableNode(ns.ElementId, ns.FullName, prompt, ["Namespace"]);
        }).ToList();
    }

    public List<EmbeddableNode> BuildProjectNodes(List<AggregationData> data)
    {
        return data.Select(p =>
        {
            var membersText = string.Join("\n- ", p.ChildSummaries);
            var prompt = $"""
                Summarize project {p.FullName} for a code intelligence graph.

                Given these namespaces and their summaries, explain:
                - What is this project's core purpose?
                - What are the main workflows or pipelines?
                - How do the namespaces layer together?

                Rules:
                - Lead with the business purpose
                - Describe the architecture and key data flows
                - 3-5 sentences max

                Namespaces:
                - {membersText}
                """;
            return new EmbeddableNode(p.ElementId, p.FullName, prompt, ["Project"]);
        }).ToList();
    }

    public List<EmbeddableNode> BuildSolutionNodes(List<AggregationData> data)
    {
        return data.Select(s =>
        {
            var membersText = string.Join("\n- ", s.ChildSummaries);
            var prompt = $"""
                Write a 1-2 sentence elevator pitch for solution {s.FullName}.

                This will be used by an LLM to decide whether to search this codebase. Focus on: what domain it serves, what it does, and what makes it distinctive.

                Projects:
                - {membersText}
                """;
            return new EmbeddableNode(s.ElementId, s.FullName, prompt, ["Solution"]);
        }).ToList();
    }
}
