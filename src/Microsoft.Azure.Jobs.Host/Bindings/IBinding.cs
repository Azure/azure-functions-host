using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host.Bindings
{
    internal interface IBinding : IArgumentBinding
    {
        BindResult Bind(BindingContext context);

        ParameterDescriptor ToParameterDescriptor();
    }
}
