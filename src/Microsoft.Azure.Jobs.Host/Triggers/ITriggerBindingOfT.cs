using Microsoft.Azure.Jobs.Host.Bindings;

namespace Microsoft.Azure.Jobs.Host.Triggers
{
    internal interface ITriggerBinding<TTriggerValue> : ITriggerBinding
    {
        ITriggerData Bind(TTriggerValue value, FunctionBindingContext context);
    }
}
