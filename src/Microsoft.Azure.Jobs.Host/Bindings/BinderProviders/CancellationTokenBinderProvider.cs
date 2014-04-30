using System;
using System.Reflection;
using System.Threading;

namespace Microsoft.Azure.Jobs.Host.Bindings.BinderProviders
{
    internal class CancellationTokenBinderProvider : ICloudBinderProvider
    {
        public ICloudBinder TryGetBinder(Type targetType)
        {
            if (targetType == typeof(CancellationToken))
            {
                return new CancellationTokenBinder();
            }
            return null;
        }

        class CancellationTokenBinder : ICloudBinder
        {
            public BindResult Bind(IBinderEx bindingContext, ParameterInfo parameter)
            {
                return new BindResult
                {
                    Result = bindingContext.CancellationToken
                };
            }
        }
    }
}
