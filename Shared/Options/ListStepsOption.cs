using System.CommandLine;

namespace GraphRagCli.Shared.Options;

public class ListStepsOption : Option<bool>
{
    public ListStepsOption(string name) : base(name)
    {
        Description = "List available pipeline steps and exit";
    }
}
