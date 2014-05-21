namespace Microsoft.Azure.Jobs.Host.Triggers
{
    internal interface ITriggerBindingProvider
    {
        ITriggerBinding TryCreate(TriggerBindingProviderContext context);
    }
}
