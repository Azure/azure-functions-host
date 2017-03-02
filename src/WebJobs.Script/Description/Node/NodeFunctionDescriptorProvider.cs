// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using Microsoft.Azure.WebJobs.Script.Binding;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    internal class NodeFunctionDescriptorProvider : FunctionDescriptorProvider
    {
        private readonly ICompilationServiceFactory<ICompilationService<IJavaScriptCompilation>, FunctionMetadata> _compilationServiceFactory;

        public NodeFunctionDescriptorProvider(ScriptHost host, ScriptHostConfiguration config)
            : this(host, config, new JavaScriptCompilationServiceFactory(host))
        {
        }

        public NodeFunctionDescriptorProvider(ScriptHost host, ScriptHostConfiguration config,
            ICompilationServiceFactory<ICompilationService<IJavaScriptCompilation>, FunctionMetadata> compilationServiceFactory)
            : base(host, config)
        {
            _compilationServiceFactory = compilationServiceFactory;
        }

        public override bool TryCreate(FunctionMetadata functionMetadata, out FunctionDescriptor functionDescriptor)
        {
            if (functionMetadata == null)
            {
                throw new ArgumentNullException(nameof(functionMetadata));
            }

            // We can only handle script types supported by the current compilation service factory
            if (!_compilationServiceFactory.SupportedScriptTypes.Contains(functionMetadata.ScriptType))
            {
                functionDescriptor = null;
                return false;
            }

            return base.TryCreate(functionMetadata, out functionDescriptor);
        }

        protected override IFunctionInvoker CreateFunctionInvoker(string scriptFilePath, BindingMetadata triggerMetadata, FunctionMetadata functionMetadata, Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings)
        {
            // TODO: FACAVAL - This will be replaced with the new invoker
            //ICompilationService<IJavaScriptCompilation> compilationService = _compilationServiceFactory.CreateService(functionMetadata.ScriptType, functionMetadata);
            //return new NodeFunctionInvoker(Host, triggerMetadata, functionMetadata, inputBindings, outputBindings, compilationService);
            // ICompilationService<IJavaScriptCompilation> compilationService = _compilationServiceFactory.CreateService(functionMetadata.ScriptType, functionMetadata);
            // return new NodeFunctionInvoker(Host, triggerMetadata, functionMetadata, inputBindings, outputBindings);

            return new NodeLanguageInvoker(Host, triggerMetadata, functionMetadata, inputBindings, outputBindings);
        }
    }
}
