namespace RunnerInterfaces
{
    // Results from function execution, produced by runner host. 
    // Function's side-effects (console output logging) is also captured separately.
    public class FunctionExecutionResult
    {
        // null on success. Type.FullName if the function threw an exception.
        public string ExceptionType { get; set; }
        public string ExceptionMessage { get; set; }
    }

    public class KuduFunctionExecutionResult
    {
        public FunctionExecutionResult Result { get; set; }

        // !!! Move this to be incremental. 
        public string ConsoleOutput { get; set; }
    }
}