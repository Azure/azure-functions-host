// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    /// <summary>
    /// BindingProvider for MobileApps extensions
    /// </summary>
    internal class MobileAppsScriptBindingProvider : ScriptBindingProvider
    {
        /// <inheritdoc/>
        public MobileAppsScriptBindingProvider(JobHostConfiguration config, JObject hostMetadata, TraceWriter traceWriter)
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

            if (string.Compare(context.Type, "mobileTable", StringComparison.OrdinalIgnoreCase) == 0)
            {
                binding = new MobileTableScriptBinding(context);
            }

            return binding != null;
        }

        /// <inheritdoc/>
        public override void Initialize()
        {
            Config.UseMobileApps();
        }

        private class MobileTableScriptBinding : ScriptBinding
        {
            public MobileTableScriptBinding(ScriptBindingContext context) : base(context)
            {
            }

            public override Type DefaultType
            {
                get
                {
                    if (Context.Access == FileAccess.Read)
                    {
                        return typeof(JObject);
                    }
                    else
                    {
                        return typeof(IAsyncCollector<JObject>);
                    }
                }
            }

            public override Collection<Attribute> GetAttributes()
            {
                return new Collection<Attribute>
                {
                    new MobileTableAttribute
                    {
                        TableName = Context.GetMetadataValue<string>("tableName"),
                        Id = Context.GetMetadataValue<string>("id"),
                        MobileAppUriSetting = Context.GetMetadataValue<string>("connection"),
                        ApiKeySetting = Context.GetMetadataValue<string>("apiKey")
                    }
                };
            }
        }
    }
}
