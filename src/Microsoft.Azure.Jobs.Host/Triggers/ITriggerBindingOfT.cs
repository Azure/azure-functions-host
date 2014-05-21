namespace Microsoft.Azure.Jobs.Host.Triggers
{
    internal interface ITriggerBinding<T> : ITriggerBinding
    {
        ITriggerData Bind(T value);
    }
}
