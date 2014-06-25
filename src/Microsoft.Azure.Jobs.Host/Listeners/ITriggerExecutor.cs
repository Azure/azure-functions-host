namespace Microsoft.Azure.Jobs.Host.Listeners
{
    internal interface ITriggerExecutor<TTriggerValue>
    {
        bool Execute(TTriggerValue value);
    }
}
