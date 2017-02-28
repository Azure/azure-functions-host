// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    /// <summary>
    /// Enables all Core SDK Triggers/Binders
    /// </summary>
    internal class WebJobsCoreScriptBindingProvider : ScriptBindingProvider
    {
        public WebJobsCoreScriptBindingProvider(JobHostConfiguration config, JObject hostMetadata, TraceWriter traceWriter) 
            : base(config, hostMetadata, traceWriter)
        {
        }

        public override void Initialize()
        {
            // Apply Queues configuration
            JObject configSection = (JObject)Metadata["queues"];
            JToken value = null;
            if (configSection != null)
            {
                if (configSection.TryGetValue("maxPollingInterval", out value))
                {
                    Config.Queues.MaxPollingInterval = TimeSpan.FromMilliseconds((int)value);
                }
                if (configSection.TryGetValue("batchSize", out value))
                {
                    Config.Queues.BatchSize = (int)value;
                }
                if (configSection.TryGetValue("maxDequeueCount", out value))
                {
                    Config.Queues.MaxDequeueCount = (int)value;
                }
                if (configSection.TryGetValue("newBatchThreshold", out value))
                {
                    Config.Queues.NewBatchThreshold = (int)value;
                }
                if (configSection.TryGetValue("visibilityTimeout", out value))
                {
                    Config.Queues.VisibilityTimeout = TimeSpan.Parse((string)value, CultureInfo.InvariantCulture);
                }
            }

            // Apply Blobs configuration
            Config.Blobs.CentralizedPoisonQueue = true;   // TEMP : In the next release we'll remove this and accept the core SDK default
            configSection = (JObject)Metadata["blobs"];
            value = null;
            if (configSection != null)
            {
                if (configSection.TryGetValue("centralizedPoisonQueue", out value))
                {
                    Config.Blobs.CentralizedPoisonQueue = (bool)value;
                }
            }
        
            Config.UseScriptExtensions();
        }

        public override bool TryCreate(ScriptBindingContext context, out ScriptBinding binding)
        {
            binding = null;

            if (string.Equals(context.Type, "table", StringComparison.OrdinalIgnoreCase))
            {
                binding = new TableScriptBinding(context);
            }
            else if (string.Compare(context.Type, "queueTrigger", StringComparison.OrdinalIgnoreCase) == 0 ||
                string.Compare(context.Type, "queue", StringComparison.OrdinalIgnoreCase) == 0)
            {
                binding = new QueueScriptBinding(context);
            }
            else if (string.Compare(context.Type, "blobTrigger", StringComparison.OrdinalIgnoreCase) == 0 ||
                     string.Compare(context.Type, "blob", StringComparison.OrdinalIgnoreCase) == 0)
            {
                binding = new BlobScriptBinding(context);
            }
            else if (string.Compare(context.Type, "httpTrigger", StringComparison.OrdinalIgnoreCase) == 0)
            {
                binding = new HttpScriptBinding(context);
            }
            else if (string.Compare(context.Type, "manualTrigger", StringComparison.OrdinalIgnoreCase) == 0)
            {
                binding = new ManualScriptBinding(context);
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
                    return typeof(HttpRequestMessage);
                }
            }

            public override Collection<Attribute> GetAttributes()
            {
                Collection<Attribute> attributes = new Collection<Attribute>();

                attributes.Add(new HttpTriggerAttribute
                {
                    RouteTemplate = Context.GetMetadataValue<string>("route")
                });

                return attributes;
            }
        }

        private class ManualScriptBinding : ScriptBinding
        {
            public ManualScriptBinding(ScriptBindingContext context) : base(context)
            {
            }

            public override Type DefaultType
            {
                get
                {
                    return typeof(string);
                }
            }

            public override Collection<Attribute> GetAttributes()
            {
                Collection<Attribute> attributes = new Collection<Attribute>();

                attributes.Add(new ManualTriggerAttribute());

                return attributes;
            }
        }

        private class TableScriptBinding : ScriptBinding
        {
            public TableScriptBinding(ScriptBindingContext context) : base(context)
            {
            }

            private string TableName
            {
                get { return Context.GetMetadataValue<string>("tableName"); }
            }

            private string PartitionKey
            {
                get { return Context.GetMetadataValue<string>("partitionKey"); }
            }

            private string RowKey
            {
                get { return Context.GetMetadataValue<string>("rowKey"); }
            }

            private string Filter
            {
                get { return Context.GetMetadataValue<string>("filter"); }
            }

            private int? Take
            {
                get { return Context.GetMetadataValue<int?>("take"); }
            }

            public override Collection<Attribute> GetAttributes()
            {
                Collection<Attribute> attributes = new Collection<Attribute>();

                var attribute = new TableAttribute(this.TableName, this.PartitionKey, this.RowKey);
                attribute.Filter = Filter;
                var take = this.Take;
                if (take.HasValue)
                {
                    attribute.Take = take.Value;
                }
                attributes.Add(attribute);

                string account = Context.GetMetadataValue<string>("connection");
                if (!string.IsNullOrEmpty(account))
                {
                    attributes.Add(new StorageAccountAttribute(account));
                }

                return attributes;
            }

            public override Type DefaultType
            {
                get
                {
                    var access = this.Context.Access;
                    if (access == FileAccess.Write)
                    {
                        return typeof(IAsyncCollector<JObject>);
                    }
                    else
                    {
                        if (this.PartitionKey != null && this.RowKey != null)
                        {
                            return typeof(JObject);
                        }
                        else
                        {
                            return typeof(JArray);
                        }
                    }
                }
            }
        }


        private class QueueScriptBinding : ScriptBinding
        {
            public QueueScriptBinding(ScriptBindingContext context) : base(context)
            {
            }

            public override Type DefaultType
            {
                get
                {
                    if (Context.Access == FileAccess.Read)
                    {
                        return string.Compare("binary", Context.DataType, StringComparison.OrdinalIgnoreCase) == 0
                            ? typeof(byte[]) : typeof(string);
                    }
                    else
                    {
                        return typeof(IAsyncCollector<byte[]>);
                    }
                }
            }

            public override Collection<Attribute> GetAttributes()
            {
                Collection<Attribute> attributes = new Collection<Attribute>();

                string queueName = Context.GetMetadataValue<string>("queueName");
                if (Context.IsTrigger)
                {
                    attributes.Add(new QueueTriggerAttribute(queueName));
                }
                else
                {
                    attributes.Add(new QueueAttribute(queueName));
                }

                string account = Context.GetMetadataValue<string>("connection");
                if (!string.IsNullOrEmpty(account))
                {
                    attributes.Add(new StorageAccountAttribute(account));
                }

                return attributes;
            }
        }

        private class BlobScriptBinding : ScriptBinding
        {
            public BlobScriptBinding(ScriptBindingContext context) : base(context)
            {
            }

            public override Type DefaultType
            {
                get
                {
                    return typeof(Stream);
                }
            }

            public override Collection<Attribute> GetAttributes()
            {
                Collection<Attribute> attributes = new Collection<Attribute>();

                string path = Context.GetMetadataValue<string>("path");
                if (Context.IsTrigger)
                {
                    attributes.Add(new BlobTriggerAttribute(path));
                }
                else
                {
                    attributes.Add(new BlobAttribute(path, Context.Access));
                }

                string account = Context.GetMetadataValue<string>("connection");
                if (!string.IsNullOrEmpty(account))
                {
                    attributes.Add(new StorageAccountAttribute(account));
                }

                return attributes;
            }
        }
    }
}
