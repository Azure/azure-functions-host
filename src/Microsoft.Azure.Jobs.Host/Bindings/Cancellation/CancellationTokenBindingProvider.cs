using System.Threading;

namespace Microsoft.Azure.Jobs.Host.Bindings.Cancellation
{
    internal class CancellationTokenBindingProvider : IBindingProvider
    {
        public IBinding TryCreate(BindingProviderContext context)
        {
            if (context.Parameter.ParameterType != typeof(CancellationToken))
            {
                return null;
            }

            return new CancellationTokenBinding();
        }
    }
}
