// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Config;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    internal class WorkerFunctionDescriptorProvider : FunctionDescriptorProvider
    {
        private readonly ICompilationServiceFactory<ICompilationService<IJavaScriptCompilation>, FunctionMetadata> _compilationServiceFactory;

        public WorkerFunctionDescriptorProvider(ScriptHost host, ScriptHostConfiguration config)
            : this(host, config, new JavaScriptCompilationServiceFactory(host))
        {
        }

        public WorkerFunctionDescriptorProvider(ScriptHost host, ScriptHostConfiguration config,
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
            functionDescriptor = null;
            return Host.FunctionDispatcher.TryRegister(functionMetadata)
                && base.TryCreate(functionMetadata, out functionDescriptor);
        }

        protected override IFunctionInvoker CreateFunctionInvoker(string scriptFilePath, BindingMetadata triggerMetadata, FunctionMetadata functionMetadata, Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings)
        {
            return new WorkerLanguageInvoker(Host, triggerMetadata, functionMetadata, inputBindings, outputBindings);
        }
    }
}
