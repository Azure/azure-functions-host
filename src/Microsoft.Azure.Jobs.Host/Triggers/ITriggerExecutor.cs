namespace Microsoft.Azure.Jobs.Host.Triggers
{
    internal interface ITriggerExecutor<TTriggerValue>
    {
        bool Execute(TTriggerValue value);
    }
}
