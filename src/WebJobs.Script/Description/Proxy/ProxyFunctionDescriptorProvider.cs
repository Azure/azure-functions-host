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
    internal sealed class ProxyFunctionDescriptorProvider : FunctionDescriptorProvider, IDisposable
    {
        public ProxyFunctionDescriptorProvider(ScriptHost host, ScriptHostConfiguration config)
            : base(host, config)
        {
        }

        public override bool TryCreate(FunctionMetadata functionMetadata, out FunctionDescriptor functionDescriptor)
        {
            if (functionMetadata == null)
            {
                throw new ArgumentNullException("functionMetadata");
            }

            functionDescriptor = null;

            if (!(functionMetadata is ProxyMetadata))
            {
                return false;
            }

            if (functionMetadata.ScriptType == ScriptType.Proxy)
            {
                functionDescriptor = new FunctionDescriptor(functionMetadata.Name, null, functionMetadata, null, null, null, null);

                return true;
            }

            return false;
        }

        protected override IFunctionInvoker CreateFunctionInvoker(string scriptFilePath, BindingMetadata triggerMetadata, FunctionMetadata functionMetadata, Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }
    }
}
