namespace Microsoft.Azure.Jobs.Host.Executors
{
    internal interface IFunctionExecutor
    {
        bool Execute(IFunctionInstance instance);
    }
}
