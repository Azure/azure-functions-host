// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    internal class MultiLanguageFunctionDescriptorProvider : WorkerFunctionDescriptorProvider
    {
        private readonly IList<RpcWorkerConfig> _workerConfig;

        public MultiLanguageFunctionDescriptorProvider(ScriptHost host, IList<RpcWorkerConfig> workerConfig, ScriptJobHostOptions config, ICollection<IScriptBindingProvider> bindingProviders,
            IFunctionInvocationDispatcher dispatcher, ILoggerFactory loggerFactory, IApplicationLifetime applicationLifetime, TimeSpan workerInitializationTimeout)
            : base(host, config, bindingProviders, dispatcher, loggerFactory, applicationLifetime, workerInitializationTimeout)
        {
            _workerConfig = workerConfig;
        }

        public override async Task<(bool, FunctionDescriptor)> TryCreate(FunctionMetadata functionMetadata)
        {
            if (functionMetadata == null)
            {
                throw new ArgumentNullException(nameof(functionMetadata));
            }

            if (!Utility.IsFunctionMetadataLanguageSupportedByWorkerRuntime(functionMetadata, _workerConfig))
            {
                return (false, null);
            }

            return await base.TryCreate(functionMetadata);
        }
    }
}
