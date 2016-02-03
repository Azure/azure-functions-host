// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Azure.WebJobs.Script.Binding;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    internal class ScriptFunctionDescriptorProvider : FunctionDescriptorProvider
    {
        public ScriptFunctionDescriptorProvider(ScriptHost host, ScriptHostConfiguration config)
            : base (host, config)
        {
        }

        public override bool TryCreate(FunctionMetadata functionMetadata, out FunctionDescriptor functionDescriptor)
        {
            functionDescriptor = null;

            string extension = Path.GetExtension(functionMetadata.Source).ToLower();
            if (!ScriptFunctionInvoker.IsSupportedScriptType(extension))
            {
                return false;
            }

            return base.TryCreate(functionMetadata, out functionDescriptor);
        }

        protected override IFunctionInvoker CreateFunctionInvoker(string scriptFilePath, BindingMetadata triggerMetadata, FunctionMetadata functionMetadata, bool omitInputParameter, Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings)
        {
            return new ScriptFunctionInvoker(scriptFilePath, Config, triggerMetadata, functionMetadata, omitInputParameter, inputBindings, outputBindings);
        }
    }
}
