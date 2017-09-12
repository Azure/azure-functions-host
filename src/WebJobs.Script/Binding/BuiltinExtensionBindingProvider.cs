// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    /// <summary>
    /// We have a backwards compat requirement to whitelist #r references to certain "builtin" dlls.
    /// Hook into #r resolution pipeline and apply the whitelist.
    /// </summary>
    internal class BuiltinExtensionBindingProvider : ScriptBindingProvider
    {
        // For backwards compat, we support a #r directly to these assemblies.
        private static HashSet<string> _assemblyWhitelist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Twilio.Api" },
                { "Microsoft.Azure.WebJobs.Extensions.Twilio" },
                { "Microsoft.Azure.NotificationHubs" },
                { "Microsoft.WindowsAzure.Mobile" },
                { "Microsoft.Azure.WebJobs.Extensions.MobileApps" },
                { "Microsoft.Azure.WebJobs.Extensions.NotificationHubs" },
                { "Microsoft.WindowsAzure.Mobile" },
                { "Microsoft.Azure.WebJobs.Extensions.MobileApps" },
                { "Microsoft.Azure.Documents.Client" },
                { "Microsoft.Azure.WebJobs.Extensions.DocumentDB" },
                { "Microsoft.Azure.ApiHub.Sdk" },
                { "Microsoft.Azure.WebJobs.Extensions.ApiHub" },
                { "Microsoft.ServiceBus" },
                { "Sendgrid" },
            };

        public BuiltinExtensionBindingProvider(JobHostConfiguration config, JObject hostMetadata, TraceWriter traceWriter)
            : base(config, hostMetadata, traceWriter)
        {
        }

        public override bool TryCreate(ScriptBindingContext context, out ScriptBinding binding)
        {
            binding = null;
            return false;
        }

        public override bool TryResolveAssembly(string assemblyName, out Assembly assembly)
        {
            if (_assemblyWhitelist.Contains(assemblyName))
            {
                assembly = Assembly.Load(assemblyName);
                return true;
            }

            return base.TryResolveAssembly(assemblyName, out assembly);
        }
    }
}
