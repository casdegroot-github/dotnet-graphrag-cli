using System.CommandLine;

namespace GraphRagCli.Shared.Options;

public class StepOption : Option<string[]>
{
    public StepOption(string name) : base(name)
    {
        Description = "Run only these pipeline steps (can specify multiple)";
        AllowMultipleArgumentsPerToken = true;
    }
}
