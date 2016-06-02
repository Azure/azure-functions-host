// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Binding;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class PowerShellFunctionDescriptorProvider : FunctionDescriptorProvider
    {
        public PowerShellFunctionDescriptorProvider(ScriptHost host, ScriptHostConfiguration config)
            : base(host, config)
        {
        }

        protected override string DefaultInputParameterName
        {
            get
            {
                // since $input is reserved in PowerShell, we have to override
                // the default
                return "inputData";
            }
        }

        public override bool TryCreate(FunctionMetadata functionMetadata, out FunctionDescriptor functionDescriptor)
        {
            if (functionMetadata == null)
            {
                throw new ArgumentNullException("functionMetadata");
            }

            functionDescriptor = null;

            if (functionMetadata.ScriptType != ScriptType.PowerShell)
            {
                return false;
            }

            foreach (var binding in functionMetadata.Bindings.Where(p => p.IsTrigger))
            {
                if (!string.IsNullOrEmpty(binding.Name) &&
                    string.Equals(binding.Name, "input", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException("Input binding name 'input' is not allowed.");
                }
            }

            return base.TryCreate(functionMetadata, out functionDescriptor);
        }

        protected override IFunctionInvoker CreateFunctionInvoker(string scriptFilePath, BindingMetadata triggerMetadata, FunctionMetadata functionMetadata, Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings)
        {
            return new PowerShellFunctionInvoker(Host, functionMetadata, inputBindings, outputBindings);
        }
    }
}
