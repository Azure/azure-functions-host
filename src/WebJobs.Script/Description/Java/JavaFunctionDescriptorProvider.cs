// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Binding;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    internal class JavaFunctionDescriptorProvider : FunctionDescriptorProvider
    {
        public JavaFunctionDescriptorProvider(ScriptHost host, ScriptHostConfiguration config)
            : base(host, config)
        {
        }

        public override bool TryCreate(FunctionMetadata functionMetadata, out FunctionDescriptor functionDescriptor)
        {
            if (functionMetadata == null)
            {
                throw new ArgumentNullException(nameof(functionMetadata));
            }

            // We can only handle script types supported by the current compilation service factory
            if (functionMetadata.ScriptType != ScriptType.JavaArchive)
            {
                functionDescriptor = null;
                return false;
            }

            return base.TryCreate(functionMetadata, out functionDescriptor);
        }

        protected override IFunctionInvoker CreateFunctionInvoker(string scriptFilePath, BindingMetadata triggerMetadata, FunctionMetadata functionMetadata, Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings)
        {
            // ICompilationService<IJavaScriptCompilation> compilationService = _compilationServiceFactory.CreateService(functionMetadata.ScriptType, functionMetadata);
            // return new NodeFunctionInvoker(Host, triggerMetadata, functionMetadata, inputBindings, outputBindings);

            return new JavaLanguageInvoker(Host, triggerMetadata, functionMetadata, inputBindings, outputBindings);
        }
    }
}
