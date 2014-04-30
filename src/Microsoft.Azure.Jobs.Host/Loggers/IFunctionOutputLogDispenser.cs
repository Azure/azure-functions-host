namespace Microsoft.Azure.Jobs
{
    // Interface for creating objects that capture a function execution's Console output. 
    internal interface IFunctionOuputLogDispenser
    {
        FunctionOutputLog CreateLogStream(FunctionInvokeRequest request);
    }
}
