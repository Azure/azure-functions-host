namespace Microsoft.WindowsAzure.Jobs
{
    internal interface IUrlFunctionLocation
    {
        // To invoke, POST to this URL, with FunctionInvokeRequest as the body. 
        string InvokeUrl { get; }
    }
}
