// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Host.Indexers
{
    internal class FunctionIndexProvider : IFunctionIndexProvider
    {
        private readonly ITypeLocator _typeLocator;
        private readonly ITriggerBindingProvider _triggerBindingProvider;
        private readonly IBindingProvider _bindingProvider;
        private readonly IJobActivator _activator;
        private readonly IFunctionExecutor _executor;
        private readonly IExtensionRegistry _extensions;
        private readonly SingletonManager _singletonManager;
        private readonly TraceWriter _trace;
        private readonly ILoggerFactory _loggerFactory;

        private IFunctionIndex _index;

        public FunctionIndexProvider(ITypeLocator typeLocator,
            ITriggerBindingProvider triggerBindingProvider,
            IBindingProvider bindingProvider,
            IJobActivator activator,
            IFunctionExecutor executor,
            IExtensionRegistry extensions,
            SingletonManager singletonManager,
            TraceWriter trace,
            ILoggerFactory loggerFactory)
        {
            if (typeLocator == null)
            {
                throw new ArgumentNullException("typeLocator");
            }

            if (triggerBindingProvider == null)
            {
                throw new ArgumentNullException("triggerBindingProvider");
            }

            if (bindingProvider == null)
            {
                throw new ArgumentNullException("bindingProvider");
            }

            if (activator == null)
            {
                throw new ArgumentNullException("activator");
            }

            if (executor == null)
            {
                throw new ArgumentNullException("executor");
            }

            if (extensions == null)
            {
                throw new ArgumentNullException("extensions");
            }

            if (singletonManager == null)
            {
                throw new ArgumentNullException("singletonManager");
            }

            if (trace == null)
            {
                throw new ArgumentNullException("trace");
            }

            _typeLocator = typeLocator;
            _triggerBindingProvider = triggerBindingProvider;
            _bindingProvider = bindingProvider;
            _activator = activator;
            _executor = executor;
            _extensions = extensions;
            _singletonManager = singletonManager;
            _trace = trace;
            _loggerFactory = loggerFactory;
        }

        public async Task<IFunctionIndex> GetAsync(CancellationToken cancellationToken)
        {
            if (_index == null)
            {
                _index = await CreateAsync(cancellationToken);
            }

            return _index;
        }

        private async Task<IFunctionIndex> CreateAsync(CancellationToken cancellationToken)
        {
            FunctionIndex index = new FunctionIndex();
            FunctionIndexer indexer = new FunctionIndexer(_triggerBindingProvider, _bindingProvider, _activator, _executor, _extensions, _singletonManager, _trace, _loggerFactory);
            IReadOnlyList<Type> types = _typeLocator.GetTypes();

            foreach (Type type in types)
            {
                await indexer.IndexTypeAsync(type, index, cancellationToken);
            }

            return index;
        }
    }
}
