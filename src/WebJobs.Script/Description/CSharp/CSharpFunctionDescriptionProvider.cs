// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Azure.WebJobs.Script.Binding;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    internal class CSharpFunctionDescriptionProvider : FunctionDescriptorProvider
    {
        private readonly FunctionAssemblyLoader _assemblyLoader;

        public CSharpFunctionDescriptionProvider(ScriptHost host, ScriptHostConfiguration config)
            : base(host, config)
        {
            _assemblyLoader = new FunctionAssemblyLoader();
        }

        public override bool TryCreate(FunctionMetadata functionMetadata, out FunctionDescriptor functionDescriptor)
        {
            functionDescriptor = null;

            string extension = Path.GetExtension(functionMetadata.Source).ToLower();
            if (!(extension == ".cs" || extension == ".csx" || string.IsNullOrEmpty(extension)))
            {
                return false;
            }

            return base.TryCreate(functionMetadata, out functionDescriptor);
        }

        protected override IFunctionInvoker CreateFunctionInvoker(string scriptFilePath, BindingMetadata triggerMetadata, FunctionMetadata functionMetadata, bool omitInputParameter, Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings)
        {
            return new CSharpFunctionInvoker(Host, triggerMetadata, functionMetadata, omitInputParameter, inputBindings, outputBindings, new FunctionEntryPointResolver(), _assemblyLoader);
        }
    }
}
