// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection.Emit;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    public abstract class FunctionBinding
    {
        private readonly ScriptHostConfiguration _config;

        protected FunctionBinding(ScriptHostConfiguration config, string name, BindingType type, FileAccess access, bool isTrigger)
        {
            _config = config;
            Name = name;
            Type = type;
            Access = access;
            IsTrigger = isTrigger;
        }

        public string Name { get; private set; }

        public BindingType Type { get; private set; }

        public bool IsTrigger { get; private set; }

        public FileAccess Access { get; private set; }

        public abstract bool HasBindingParameters { get; }

        public abstract Task BindAsync(BindingContext context);

        public abstract CustomAttributeBuilder GetCustomAttribute();

        internal static Collection<FunctionBinding> GetBindings(ScriptHostConfiguration config, IEnumerable<BindingMetadata> bindingMetadatas, FileAccess fileAccess)
        {
            Collection<FunctionBinding> bindings = new Collection<FunctionBinding>();

            if (bindings != null)
            {
                foreach (var bindingMetadata in bindingMetadatas)
                {
                    BindingType type = bindingMetadata.Type;
                    string name = bindingMetadata.Name;

                    switch (type)
                    {
                        case BindingType.Blob:
                        case BindingType.BlobTrigger:
                            BlobBindingMetadata blobBindingMetadata = (BlobBindingMetadata)bindingMetadata;
                            bindings.Add(new BlobBinding(config, name, blobBindingMetadata.Path, fileAccess, bindingMetadata.IsTrigger));
                            break;
                        case BindingType.EventHub:
                        case BindingType.EventHubTrigger:
                            EventHubBindingMetadata eventHubBindingMetadata = (EventHubBindingMetadata)bindingMetadata;
                            if (!eventHubBindingMetadata.IsTrigger &&
                                fileAccess != FileAccess.Write)
                            {
                                throw new InvalidOperationException("EventHub binding can only be used for output.");
                            }
                            bindings.Add(new EventHubBinding(config, name, eventHubBindingMetadata.Path, fileAccess, bindingMetadata.IsTrigger));
                            break;
                        case BindingType.Queue:
                        case BindingType.QueueTrigger:
                            QueueBindingMetadata queueBindingMetadata = (QueueBindingMetadata)bindingMetadata;
                            if (!queueBindingMetadata.IsTrigger &&
                                fileAccess != FileAccess.Write)
                            {
                                throw new InvalidOperationException("Queue binding can only be used for output.");
                            }
                            bindings.Add(new QueueBinding(config, name, queueBindingMetadata.QueueName, fileAccess, bindingMetadata.IsTrigger));
                            break;
                        case BindingType.ServiceBus:
                        case BindingType.ServiceBusTrigger:
                            ServiceBusBindingMetadata serviceBusBindingMetadata = (ServiceBusBindingMetadata)bindingMetadata;
                            if (!serviceBusBindingMetadata.IsTrigger &&
                                fileAccess != FileAccess.Write)
                            {
                                throw new InvalidOperationException("ServiceBus binding can only be used for output.");
                            }
                            string queueOrTopicName = serviceBusBindingMetadata.QueueName ?? serviceBusBindingMetadata.TopicName;
                            bindings.Add(new ServiceBusBinding(config, name, queueOrTopicName, fileAccess, bindingMetadata.IsTrigger));
                            break;
                        case BindingType.Table:
                            TableBindingMetadata tableBindingMetadata = (TableBindingMetadata)bindingMetadata;
                            TableQuery tableQuery = new TableQuery
                            {
                                TakeCount = tableBindingMetadata.Take,
                                FilterString = tableBindingMetadata.Filter
                            };
                            bindings.Add(new TableBinding(config, name, tableBindingMetadata.TableName, tableBindingMetadata.PartitionKey, tableBindingMetadata.RowKey, fileAccess, tableQuery));
                            break;
                        case BindingType.Http:
                            if (fileAccess != FileAccess.Write)
                            {
                                throw new InvalidOperationException("Http binding can only be used for output.");
                            }
                            // TODO: Why is this here?
                            name = name ?? "res";
                            bindings.Add(new HttpBinding(config, name, FileAccess.Write, bindingMetadata.IsTrigger));
                            break;
                        case BindingType.HttpTrigger:
                            bindings.Add(new HttpBinding(config, name, FileAccess.Read, bindingMetadata.IsTrigger));
                            break;
                        case BindingType.EasyTable:
                            EasyTableBindingMetadata easyTableMetadata = (EasyTableBindingMetadata)bindingMetadata;
                            bindings.Add(new EasyTableBinding(config, name, easyTableMetadata.TableName, easyTableMetadata.Id, fileAccess, bindingMetadata.Direction));
                            break;
                        case BindingType.DocumentDB:
                            DocumentDBBindingMetadata docDBMetadata = (DocumentDBBindingMetadata)bindingMetadata;
                            bindings.Add(new DocumentDBBinding(config, name, docDBMetadata.DatabaseName, docDBMetadata.CollectionName, docDBMetadata.CreateIfNotExists, fileAccess, bindingMetadata.Direction));
                            break;
                        case BindingType.NotificationHub:
                            NotificationHubBindingMetadata notificationHubMetadata = (NotificationHubBindingMetadata)bindingMetadata;
                            bindings.Add(new NotificationHubBinding(config, name, notificationHubMetadata.TagExpression, fileAccess, bindingMetadata.Direction));
                            break;
                    }
                }
            }

            return bindings;
        }

        protected string Resolve(string name)
        {
            if (_config.HostConfig.NameResolver == null)
            {
                return name;
            }

            return _config.HostConfig.NameResolver.ResolveWholeString(name);
        }
    }
}
