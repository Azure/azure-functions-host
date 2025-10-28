// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    internal class HttpFunctionDescriptorProvider : WorkerFunctionDescriptorProvider
    {
        public HttpFunctionDescriptorProvider(ScriptJobHostOptions config, ICollection<IScriptBindingProvider> bindingProviders,
            IFunctionInvocationDispatcher dispatcher, ILoggerFactory loggerFactory, IApplicationLifetime applicationLifetime, IScriptEventManager eventManager, bool isExtensionBundleConfigured, TimeSpan workerInitializationTimeout)
            : base(config, bindingProviders, dispatcher, loggerFactory, applicationLifetime, eventManager, isExtensionBundleConfigured, workerInitializationTimeout)
        {
        }
    }
}
