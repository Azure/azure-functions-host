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

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    public abstract class FunctionBinding
    {
        private readonly ScriptHostConfiguration _config;

        protected FunctionBinding(ScriptHostConfiguration config, BindingMetadata metadata, FileAccess access)
        {
            _config = config;
            Access = access;
            Metadata = metadata;
        }

        public BindingMetadata Metadata { get; private set; }

        public FileAccess Access { get; private set; }

        public abstract bool HasBindingParameters { get; }

        public abstract Task BindAsync(BindingContext context);

        public abstract Collection<CustomAttributeBuilder> GetCustomAttributes();

        internal static Collection<FunctionBinding> GetBindings(ScriptHostConfiguration config, IEnumerable<BindingMetadata> bindingMetadatas, FileAccess fileAccess)
        {
            Collection<FunctionBinding> bindings = new Collection<FunctionBinding>();

            if (bindings != null)
            {
                foreach (var bindingMetadata in bindingMetadatas)
                {
                    switch (bindingMetadata.Type)
                    {
                        case BindingType.Blob:
                        case BindingType.BlobTrigger:
                            BlobBindingMetadata blobBindingMetadata = (BlobBindingMetadata)bindingMetadata;
                            bindings.Add(new BlobBinding(config, blobBindingMetadata, fileAccess));
                            break;
                        case BindingType.EventHub:
                        case BindingType.EventHubTrigger:
                            EventHubBindingMetadata eventHubBindingMetadata = (EventHubBindingMetadata)bindingMetadata;
                            if (!eventHubBindingMetadata.IsTrigger &&
                                fileAccess != FileAccess.Write)
                            {
                                throw new InvalidOperationException("EventHub binding can only be used for output.");
                            }
                            bindings.Add(new EventHubBinding(config, eventHubBindingMetadata, fileAccess));
                            break;
                        case BindingType.Queue:
                        case BindingType.QueueTrigger:
                            QueueBindingMetadata queueBindingMetadata = (QueueBindingMetadata)bindingMetadata;
                            if (!queueBindingMetadata.IsTrigger &&
                                fileAccess != FileAccess.Write)
                            {
                                throw new InvalidOperationException("Queue binding can only be used for output.");
                            }
                            bindings.Add(new QueueBinding(config, queueBindingMetadata, fileAccess));
                            break;
                        case BindingType.ServiceBus:
                        case BindingType.ServiceBusTrigger:
                            ServiceBusBindingMetadata serviceBusBindingMetadata = (ServiceBusBindingMetadata)bindingMetadata;
                            if (!serviceBusBindingMetadata.IsTrigger &&
                                fileAccess != FileAccess.Write)
                            {
                                throw new InvalidOperationException("ServiceBus binding can only be used for output.");
                            }
                            bindings.Add(new ServiceBusBinding(config, serviceBusBindingMetadata, fileAccess));
                            break;
                        case BindingType.Table:
                            TableBindingMetadata tableBindingMetadata = (TableBindingMetadata)bindingMetadata;
                            bindings.Add(new TableBinding(config, tableBindingMetadata, fileAccess));
                            break;
                        case BindingType.Http:
                            if (fileAccess != FileAccess.Write)
                            {
                                throw new InvalidOperationException("Http binding can only be used for output.");
                            }
                            if (string.IsNullOrEmpty(bindingMetadata.Name))
                            {
                                // TODO: Why is this here?
                                bindingMetadata.Name = "res";
                            }
                            bindings.Add(new HttpBinding(config, bindingMetadata, FileAccess.Write));
                            break;
                        case BindingType.HttpTrigger:
                            bindings.Add(new HttpBinding(config, bindingMetadata, FileAccess.Read));
                            break;
                        case BindingType.EasyTable:
                            EasyTableBindingMetadata easyTableMetadata = (EasyTableBindingMetadata)bindingMetadata;
                            bindings.Add(new EasyTableBinding(config, easyTableMetadata, fileAccess));
                            break;
                        case BindingType.DocumentDB:
                            DocumentDBBindingMetadata docDBMetadata = (DocumentDBBindingMetadata)bindingMetadata;
                            bindings.Add(new DocumentDBBinding(config, docDBMetadata, fileAccess));
                            break;
                        case BindingType.NotificationHub:
                            NotificationHubBindingMetadata notificationHubMetadata = (NotificationHubBindingMetadata)bindingMetadata;
                            bindings.Add(new NotificationHubBinding(config, notificationHubMetadata, fileAccess));
                            break;
                        case BindingType.ApiHub:
                        case BindingType.ApiHubTrigger:
                            ApiHubBindingMetadata apiHubBindingMetadata = (ApiHubBindingMetadata)bindingMetadata;
                            apiHubBindingMetadata.Key = Guid.NewGuid().ToString();
                            bindings.Add(new ApiHubBinding(config, apiHubBindingMetadata, fileAccess));
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

        internal static void AddStorageAccountAttribute(Collection<CustomAttributeBuilder> attributes, string connection)
        {
            var constructorTypes = new Type[] { typeof(string) };
            var constructorArguments = new object[] { connection };
            var attribute = new CustomAttributeBuilder(typeof(StorageAccountAttribute).GetConstructor(constructorTypes), constructorArguments);
            attributes.Add(attribute);
        }
    }
}
