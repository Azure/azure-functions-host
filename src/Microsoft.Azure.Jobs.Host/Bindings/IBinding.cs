using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host.Bindings
{
    internal interface IBinding
    {
        bool FromAttribute { get; }

        IValueProvider Bind(object value, ArgumentBindingContext context);

        IValueProvider Bind(BindingContext context);

        ParameterDescriptor ToParameterDescriptor();
    }
}
