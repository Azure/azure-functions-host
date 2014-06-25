using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Executors;

namespace Microsoft.Azure.Jobs.Host.Runners
{
    internal class InvokeBindingSource : IBindingSource
    {
        private readonly IFunctionBinding _functionBinding;
        private readonly IDictionary<string, object> _parameters;

        public InvokeBindingSource(IFunctionBinding functionBinding, IDictionary<string, object> parameters)
        {
            _functionBinding = functionBinding;
            _parameters = parameters;
        }

        public IReadOnlyDictionary<string, IValueProvider> Bind(FunctionBindingContext context)
        {
            return _functionBinding.Bind(context, _parameters);
        }
    }
}
