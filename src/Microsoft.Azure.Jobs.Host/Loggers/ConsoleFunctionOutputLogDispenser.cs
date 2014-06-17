namespace Microsoft.Azure.Jobs
{
    // Implementation of IFunctionOuputLogDispenser that just logs to the console. 
    class ConsoleFunctionOuputLogDispenser : IFunctionOutputLogDispenser
    {
        public FunctionOutputLog CreateLogStream(FunctionInvokeRequest request)
        {
            return new FunctionOutputLog();
        }
    }
}
