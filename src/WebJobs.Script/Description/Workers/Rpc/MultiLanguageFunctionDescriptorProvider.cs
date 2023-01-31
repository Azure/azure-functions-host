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

        /// <summary>
        /// Initializes a new instance of the <see cref="MultiLanguageFunctionDescriptorProvider"/> class.
        /// </summary>
        /// <param name="host"><see cref="ScriptHost"/> instance.</param>
        /// <param name="workerConfig">All supported <see cref="RpcWorkerConfig"/>.</param>
        /// <param name="config"><see cref="ScriptJobHostOptions"/> instance.</param>
        /// <param name="bindingProviders">List of <see cref="IScriptBindingProvider"/> instances.</param>
        /// <param name="dispatcher"><see cref="IFunctionInvocationDispatcher"/> instance.</param>
        /// <param name="loggerFactory"><see cref="ILoggerFactory"/> instance.</param>
        /// <param name="applicationLifetime"><see cref="IApplicationLifetime"/> instance.</param>
        /// <param name="workerInitializationTimeout">Worker initialization timeout.</param>
        public MultiLanguageFunctionDescriptorProvider(ScriptHost host, IList<RpcWorkerConfig> workerConfig, ScriptJobHostOptions config, ICollection<IScriptBindingProvider> bindingProviders,
            IFunctionInvocationDispatcher dispatcher, ILoggerFactory loggerFactory, IApplicationLifetime applicationLifetime, TimeSpan workerInitializationTimeout)
            : base(host, config, bindingProviders, dispatcher, loggerFactory, applicationLifetime, workerInitializationTimeout)
        {
            _workerConfig = workerConfig ?? throw new ArgumentNullException(nameof(workerConfig));
        }

        /// <inheritdoc/>
        public override async Task<(bool Success, FunctionDescriptor Descriptor)> TryCreate(FunctionMetadata functionMetadata)
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
