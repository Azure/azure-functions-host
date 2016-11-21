// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Binding.Http;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script
{
    // Expose core extensions 
    public class CoreExtensionBase : ExtensionBase
    {
        protected override IEnumerable<Type> ExposedAttributes
        {
            get
            {
                return new Type[]
                {
                    typeof(HttpTriggerAttribute),
                    typeof(ManualTriggerAttribute)
                };
            }
        }

        public override Task InitAsync(JobHostConfiguration config, JObject metadata)
        {
            config.UseScriptExtensions();

            return Task.FromResult(0);
        }

        public override Type GetDefaultType(FileAccess access, Cardinality cardinality, DataType dataType, Attribute attribute)
        {
            // Once binders are rule-based, we can get rid of this method. 
            if (attribute.GetType() == typeof(HttpTriggerAttribute))
            {
                return typeof(HttpRequestMessage);
            }
            return typeof(string);
        }
    }    
}
