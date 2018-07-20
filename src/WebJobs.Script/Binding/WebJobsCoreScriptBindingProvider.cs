// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    /// <summary>
    /// Enables all Core SDK Triggers/Binders
    /// </summary>
    internal class WebJobsCoreScriptBindingProvider : ScriptBindingProvider
    {
        public WebJobsCoreScriptBindingProvider(IOptions<JobHostOptions> options, JObject hostMetadata, ILogger logger)
            : base(options, hostMetadata, logger)
        {
        }

        public override bool TryCreate(ScriptBindingContext context, out ScriptBinding binding)
        {
            binding = null;

            if (string.Compare(context.Type, "blobTrigger", StringComparison.OrdinalIgnoreCase) == 0 ||
                string.Compare(context.Type, "blob", StringComparison.OrdinalIgnoreCase) == 0)
            {
                // TODO: DI (FACAVAL) Load storage extensions dynamically
                //binding = new BlobScriptBinding(context);
            }
            else if (string.Compare(context.Type, "httpTrigger", StringComparison.OrdinalIgnoreCase) == 0)
            {
                binding = new HttpScriptBinding(context);
            }

            return binding != null;
        }

        private class HttpScriptBinding : ScriptBinding
        {
            public HttpScriptBinding(ScriptBindingContext context) : base(context)
            {
            }

            public override Type DefaultType
            {
                get
                {
                    return typeof(HttpRequest);
                }
            }

            public override Collection<Attribute> GetAttributes()
            {
                var authLevel = Context.GetMetadataEnumValue<AuthorizationLevel>("authLevel", AuthorizationLevel.Function);

                JArray methodArray = Context.GetMetadataValue<JArray>("methods");
                string[] methods = null;
                if (methodArray != null)
                {
                    methods = methodArray.Select(p => p.Value<string>()).ToArray();
                }

                var attribute = new HttpTriggerAttribute(authLevel, methods)
                {
                    Route = Context.GetMetadataValue<string>("route"),
                    WebHookType = Context.GetMetadataValue<string>("webHookType")
                };

                return new Collection<Attribute> { attribute };
            }
        }

        // TODO: DI (FACAVAL) Load storage extensions dynamically
        //private class BlobScriptBinding : ScriptBinding
        //{
        //    public BlobScriptBinding(ScriptBindingContext context) : base(context)
        //    {
        //    }

        //    public override Type DefaultType
        //    {
        //        get
        //        {
        //            return typeof(Stream);
        //        }
        //    }

        //    public override Collection<Attribute> GetAttributes()
        //    {
        //        Collection<Attribute> attributes = new Collection<Attribute>();

        //        string path = Context.GetMetadataValue<string>("path");
        //        Attribute attribute = null;
        //        if (Context.IsTrigger)
        //        {
        //            attribute = new BlobTriggerAttribute(path);
        //        }
        //        else
        //        {
        //            attribute = new BlobAttribute(path, Context.Access);
        //        }
        //        attributes.Add(attribute);

        //        var connectionProvider = (IConnectionProvider)attribute;
        //        string connection = Context.GetMetadataValue<string>("connection");
        //        if (!string.IsNullOrEmpty(connection))
        //        {
        //            connectionProvider.Connection = connection;
        //        }

        //        return attributes;
        //    }
        //}
    }
}
