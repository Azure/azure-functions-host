namespace Microsoft.WindowsAzure.Jobs
{
    // Results from function execution, produced by runner host. 
    // Function's side-effects (console output logging) is also captured separately.
    internal class FunctionExecutionResult
    {
        // null on success. Type.FullName if the function threw an exception.
        public string ExceptionType { get; set; }
        public string ExceptionMessage { get; set; }
    }
}
