// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks.Dataflow;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Dispatch;
using Microsoft.Azure.WebJobs.Script.Description.Script;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    internal class WorkerFunctionDescriptorProvider : FunctionDescriptorProvider
    {
        private IFunctionDispatcher _dispatcher;

        public WorkerFunctionDescriptorProvider(ScriptHost host, ScriptHostConfiguration config, IFunctionDispatcher dispatcher)
            : base(host, config)
        {
            _dispatcher = dispatcher;
        }

        public override bool TryCreate(FunctionMetadata functionMetadata, out FunctionDescriptor functionDescriptor)
        {
            if (functionMetadata == null)
            {
                throw new ArgumentNullException(nameof(functionMetadata));
            }
            functionDescriptor = null;
            return _dispatcher.IsSupported(functionMetadata)
                && base.TryCreate(functionMetadata, out functionDescriptor);
        }

        protected override IFunctionInvoker CreateFunctionInvoker(string scriptFilePath, BindingMetadata triggerMetadata, FunctionMetadata functionMetadata, Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings)
        {
            var inputBuffer = new BufferBlock<ScriptInvocationContext>();
            _dispatcher.Register(new FunctionRegistrationContext
            {
                Metadata = functionMetadata,
                InputBuffer = inputBuffer
            });
            return new WorkerLanguageInvoker(Host, triggerMetadata, functionMetadata, inputBindings, outputBindings, inputBuffer);
        }
    }
}
