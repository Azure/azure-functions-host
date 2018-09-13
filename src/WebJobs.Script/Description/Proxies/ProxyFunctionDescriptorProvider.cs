// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    internal sealed class ProxyFunctionDescriptorProvider : FunctionDescriptorProvider
    {
        private readonly ILoggerFactory _loggerFactory;
        private ProxyClientExecutor _proxyClient;

        public ProxyFunctionDescriptorProvider(ScriptHost host, ScriptJobHostOptions config, ICollection<IScriptBindingProvider> bindingProviders,
            ProxyClientExecutor proxyClient, ILoggerFactory loggerFactory)
            : base(host, config, bindingProviders)
        {
            _proxyClient = proxyClient;
            _loggerFactory = loggerFactory;
        }

        public override Task<(bool, FunctionDescriptor)> TryCreate(FunctionMetadata functionMetadata)
        {
            if (functionMetadata == null)
            {
                throw new ArgumentNullException("functionMetadata");
            }

            if (functionMetadata.IsProxy)
            {
                return base.TryCreate(functionMetadata);
            }

            return Task.FromResult<(bool, FunctionDescriptor)>((false, null));
        }

        protected override IFunctionInvoker CreateFunctionInvoker(string scriptFilePath, BindingMetadata triggerMetadata, FunctionMetadata functionMetadata, Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings)
        {
            return new ProxyFunctionInvoker(Host, functionMetadata, _proxyClient, _loggerFactory);
        }
    }
}
