namespace Microsoft.WindowsAzure.Jobs
{
    internal class KuduFunctionExecutionResult
    {
        public FunctionExecutionResult Result { get; set; }

        // $$$ Move this to be incremental.  
        public string ConsoleOutput { get; set; }
    }
}
