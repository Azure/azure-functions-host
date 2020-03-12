// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Script.Abstractions.Description;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    internal class RpcFunctionDescriptorProvider : WorkerFunctionDescriptorProvider
    {
        private readonly string _workerRuntime;

        public RpcFunctionDescriptorProvider(ScriptHost host, string workerRuntime, ScriptJobHostOptions config, ICollection<IScriptBindingProvider> bindingProviders,
            IFunctionInvocationDispatcher dispatcher, ILoggerFactory loggerFactory, IApplicationLifetime applicationLifetime)
            : base(host, config, bindingProviders, dispatcher, loggerFactory, applicationLifetime)
        {
            _workerRuntime = workerRuntime;
        }

        public override async Task<(bool, FunctionDescriptor)> TryCreate(FunctionMetadata functionMetadata)
        {
            if (functionMetadata == null)
            {
                throw new ArgumentNullException(nameof(functionMetadata));
            }

            if (!Utility.IsFunctionMetadataLanguageSupportedByWorkerRuntime(functionMetadata, _workerRuntime))
            {
                return (false, null);
            }

            return await base.TryCreate(functionMetadata);
        }
    }
}
