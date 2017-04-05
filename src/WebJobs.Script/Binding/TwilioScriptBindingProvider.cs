// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.Reflection;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Newtonsoft.Json.Linq;
using Twilio;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    /// <summary>
    /// BindingProvider for Twilio extensions
    /// </summary>
    internal class TwilioScriptBindingProvider : ScriptBindingProvider
    {
        public TwilioScriptBindingProvider(JobHostConfiguration config, JObject hostMetadata, TraceWriter traceWriter)
            : base(config, hostMetadata, traceWriter)
        {
        }

    /// <inheritdoc/>
        public override bool TryCreate(ScriptBindingContext context, out ScriptBinding binding)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }

            binding = null;

            if (string.Compare(context.Type, "twilioSMS", StringComparison.OrdinalIgnoreCase) == 0)
            {
                binding = new TwilioSmsBinding(context);
            }

            return binding != null;
        }

        /// <inheritdoc/>
        public override void Initialize()
        {
            Config.UseTwilioSms();
        }

        /// <inheritdoc/>
        public override bool TryResolveAssembly(string assemblyName, out Assembly assembly)
        {
            assembly = null;

            return Utility.TryMatchAssembly(assemblyName, typeof(SMSMessage), out assembly) ||
                   Utility.TryMatchAssembly(assemblyName, typeof(TwilioSmsAttribute), out assembly);
        }

        private class TwilioSmsBinding : ScriptBinding
        {
            public TwilioSmsBinding(ScriptBindingContext context) : base(context)
            {
            }

            public override Type DefaultType
            {
                get
                {
                    return typeof(IAsyncCollector<JObject>);
                }
            }

            public override Collection<Attribute> GetAttributes()
            {
                return new Collection<Attribute>
                {
                    new TwilioSmsAttribute
                    {
                        AccountSidSetting = Context.GetMetadataValue<string>("accountSid"),
                        AuthTokenSetting = Context.GetMetadataValue<string>("authToken"),
                        To = Context.GetMetadataValue<string>("to"),
                        From = Context.GetMetadataValue<string>("from"),
                        Body = Context.GetMetadataValue<string>("body")
                    }
                };
            }
        }
    }
}
