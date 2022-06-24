namespace WorkerHarness.Core
{
    /// <summary>
    /// Capture context to run a scenario file
    /// </summary>
    public class Instruction
    {
        // path to the scenario file
        public string Action { get; set; } = string.Empty;

        // the number of times to repeat the actions in the scenario file
        public int Repeat { get; set; } = 1;

        // the name of the function
        public string FunctionName { get; set; } = string.Empty;

        // the name of the trigger
        public string Trigger { get; set; } = string.Empty;

        // the amount of time to delay
        public int DelayIn { get; set; } = 0;
    }
}
