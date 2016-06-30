using CommandLine;

namespace WebJobs.Script.ConsoleHost.Scenarios
{
    public abstract class BaseArmScenario : Scenario
    {
        [ValueOption(0)]
        public string FunctionAppName { get; set; }
    }
}
