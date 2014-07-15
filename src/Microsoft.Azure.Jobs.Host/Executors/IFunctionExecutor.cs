namespace Microsoft.Azure.Jobs.Host.Executors
{
    internal interface IFunctionExecutor
    {
        IDelayedException TryExecute(IFunctionInstance instance);
    }
}
