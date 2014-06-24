using System;
using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Executors;
using Microsoft.Azure.Jobs.Host.Indexers;

namespace Microsoft.Azure.Jobs.Host.Runners
{
    internal class InvokeBindCommand : IBindCommand
    {
        private readonly Guid _functionInstanceId;
        private readonly FunctionDefinition _function;
        private readonly IDictionary<string, object> _parameters;
        private readonly RuntimeBindingProviderContext _context;

        public InvokeBindCommand(Guid functionInstanceId, FunctionDefinition function,
            IDictionary<string, object> parameters, RuntimeBindingProviderContext context)
        {
            _functionInstanceId = functionInstanceId;
            _function = function;
            _parameters = parameters;
            _context = context;
        }

        public IReadOnlyDictionary<string, IValueProvider> Execute()
        {
            return _function.Binding.Bind(_context, _functionInstanceId, _parameters);
        }
    }
}
