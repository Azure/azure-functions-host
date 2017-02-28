﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    /// <summary>
    /// BindingProvider for DocumentDB extensions
    /// </summary>
    internal class DocumentDBScriptBindingProvider : ScriptBindingProvider
    {
        private static readonly string DocumentDBAssemblyName = typeof(DocumentClient).Assembly.GetName().Name;

        /// <inheritdoc/>
        public DocumentDBScriptBindingProvider(JobHostConfiguration config, JObject hostMetadata, TraceWriter traceWriter) 
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

            if (string.Compare(context.Type, "documentDB", StringComparison.OrdinalIgnoreCase) == 0)
            {
                binding = new DocumentDBScriptBinding(context);
            }

            return binding != null;
        }

        /// <inheritdoc/>
        public override void Initialize()
        {
            Config.UseDocumentDB();
        }

        /// <inheritdoc/>
        public override bool TryResolveAssembly(string assemblyName, out Assembly assembly)
        {
            assembly = null;

            if (string.Compare(assemblyName, DocumentDBAssemblyName, StringComparison.OrdinalIgnoreCase) == 0)
            {
                assembly = typeof(DocumentClient).Assembly;
            }

            return assembly != null;
        }

        private class DocumentDBScriptBinding : ScriptBinding
        {
            public DocumentDBScriptBinding(ScriptBindingContext context) : base(context)
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
                Collection<Attribute> attributes = new Collection<Attribute>();

                string databaseName = Context.GetMetadataValue<string>("databaseName");
                string collectionName = Context.GetMetadataValue<string>("collectionName");

                DocumentDBAttribute attribute = null;
                if (!string.IsNullOrEmpty(databaseName) || !string.IsNullOrEmpty(collectionName))
                {
                    attribute = new DocumentDBAttribute(databaseName, collectionName);
                }
                else
                {
                    attribute = new DocumentDBAttribute();
                }

                attribute.CreateIfNotExists = Context.GetMetadataValue<bool>("createIfNotExists");
                attribute.ConnectionStringSetting = Context.GetMetadataValue<string>("connection");
                attribute.Id = Context.GetMetadataValue<string>("id");
                attribute.PartitionKey = Context.GetMetadataValue<string>("partitionKey");
                attribute.CollectionThroughput = Context.GetMetadataValue<int>("collectionThroughput");

                attributes.Add(attribute);

                return attributes;
            }
        }
    }
}
