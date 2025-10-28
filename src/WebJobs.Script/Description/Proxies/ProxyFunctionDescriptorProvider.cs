// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    internal sealed class ProxyFunctionDescriptorProvider : FunctionDescriptorProvider
    {
        private static readonly Task<(bool, FunctionDescriptor)> NilCreateFunctionInvokerResult = Task.FromResult<(bool, FunctionDescriptor)>((false, null));
        private readonly IScriptEventManager _eventManager;
        private readonly ILoggerFactory _loggerFactory;

        public ProxyFunctionDescriptorProvider(ScriptJobHostOptions config, IScriptEventManager eventManager, ICollection<IScriptBindingProvider> bindingProviders,
             bool isExtensionBundleConfigured, ILoggerFactory loggerFactory)
            : base(config, bindingProviders, isExtensionBundleConfigured, loggerFactory)
        {
            _eventManager = eventManager;
            _loggerFactory = loggerFactory;
        }

        public override Task<(bool Success, FunctionDescriptor Descriptor)> TryCreate(FunctionMetadata functionMetadata)
        {
            if (functionMetadata == null)
            {
                throw new ArgumentNullException("functionMetadata");
            }

            if (functionMetadata.IsProxy())
            {
                return base.TryCreate(functionMetadata);
            }

            return NilCreateFunctionInvokerResult;
        }

        protected override IFunctionInvoker CreateFunctionInvoker(string scriptFilePath, BindingMetadata triggerMetadata, FunctionMetadata functionMetadata, Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings)
        {
            if (!(functionMetadata is ProxyFunctionMetadata proxyFunctionMetada))
            {
                throw new InvalidCastException($"Expected {nameof(functionMetadata)} to be of type {nameof(ProxyFunctionMetadata)}");
            }
            return new ProxyFunctionInvoker(proxyFunctionMetada, Config, _eventManager, _loggerFactory);
        }
    }
}
