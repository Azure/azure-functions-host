using System.Reflection;
using System.Threading;

namespace Microsoft.Azure.Jobs.Host.Bindings.Cancellation
{
    internal class CancellationTokenBindingProvider : IBindingProvider
    {
        public IBinding TryCreate(BindingProviderContext context)
        {
            ParameterInfo parameter = context.Parameter;

            if (parameter.ParameterType != typeof(CancellationToken))
            {
                return null;
            }

            return new CancellationTokenBinding(parameter.Name);
        }
    }
}
