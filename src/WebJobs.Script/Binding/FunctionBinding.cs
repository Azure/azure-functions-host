// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Dynamic;
using System.IO;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Bindings.Runtime;
using Microsoft.Azure.WebJobs.Script.Description;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

        public abstract Task BindAsync(BindingContext context);

        public abstract Collection<CustomAttributeBuilder> GetCustomAttributes(Type parameterType);

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
                        case BindingType.MobileTable:
                            MobileTableBindingMetadata mobileTableMetadata = (MobileTableBindingMetadata)bindingMetadata;
                            bindings.Add(new MobileTableBinding(config, mobileTableMetadata, fileAccess));
                            break;
                        case BindingType.DocumentDB:
                            DocumentDBBindingMetadata docDBMetadata = (DocumentDBBindingMetadata)bindingMetadata;
                            bindings.Add(new DocumentDBBinding(config, docDBMetadata, fileAccess));
                            break;
                        case BindingType.NotificationHub:
                            NotificationHubBindingMetadata notificationHubMetadata = (NotificationHubBindingMetadata)bindingMetadata;
                            bindings.Add(new NotificationHubBinding(config, notificationHubMetadata, fileAccess));
                            break;
                        case BindingType.ApiHubFile:
                        case BindingType.ApiHubFileTrigger:
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

        internal static ICollection ReadAsCollection(object value)
        {
            ICollection values = null;

            if (value is Stream)
            {
                // first deserialize the stream as a string
                byte[] bytes;
                using (MemoryStream ms = new MemoryStream())
                {
                    ((Stream)value).CopyTo(ms);
                    bytes = ms.ToArray();
                }
                value = Encoding.UTF8.GetString(bytes);
            }

            string stringValue = value as string;
            if (Utility.IsJson(stringValue))
            {
                // json values can either be singleton objects,
                // or arrays of objects/values
                JToken token = JToken.Parse(stringValue);
                if (token.Type != JTokenType.Array)
                {
                    // not an array so create a new array and add
                    // the singleton
                    values = new JArray() { token };
                }
                else
                {
                    values = (JArray)token;
                }
            }
            else
            {
                // not json, so add the singleton value
                values = new Collection<object>() { value };
            }

            return values;
        }

        internal static async Task BindAsyncCollectorAsync<T>(BindingContext context, RuntimeBindingContext runtimeContext)
        {
            IAsyncCollector<T> collector = await context.Binder.BindAsync<IAsyncCollector<T>>(runtimeContext);

            IEnumerable values = ReadAsCollection(context.Value);

            // convert values as necessary and add to the collector
            foreach (var value in values)
            {
                object converted = null;
                if (typeof(T) == typeof(string))
                {
                    converted = value.ToString();
                }
                else if (typeof(T) == typeof(JObject))
                {
                    converted = (JObject)value;
                }
                else if (typeof(T) == typeof(byte[]))
                {
                    byte[] bytes = value as byte[];
                    if (bytes == null)
                    {
                        string stringValue = value.ToString();
                        bytes = Encoding.UTF8.GetBytes(stringValue);
                    }
                    converted = bytes;
                }
                else
                {
                    throw new ArgumentException("Unsupported collection type.");
                }

                await collector.AddAsync((T)converted);
            }
        }

        internal static async Task BindStreamAsync(BindingContext context, FileAccess access, RuntimeBindingContext runtimeContext)
        {
            Stream stream = await context.Binder.BindAsync<Stream>(runtimeContext);

            if (access == FileAccess.Write)
            {
                ConvertValueToStream(context.Value, stream);
            }
            else
            {
                // Read the value into the context Value converting based on data type
                object converted = null;
                ConvertStreamToValue(stream, context.DataType, ref converted);
                context.Value = converted;
            }
        }

        public static void ConvertValueToStream(object value, Stream stream)
        {
            Stream valueStream = value as Stream;
            if (valueStream == null)
            {
                // Convert the value to bytes and write it
                // to the stream
                byte[] bytes = null;
                Type type = value.GetType();
                if (type == typeof(byte[]))
                {
                    bytes = (byte[])value;
                }
                else if (type == typeof(string))
                {
                    bytes = Encoding.UTF8.GetBytes((string)value);
                }

                using (valueStream = new MemoryStream(bytes))
                {
                    valueStream.CopyTo(stream);
                }
            }
            else
            {
                // value is already a stream, so copy it directly
                valueStream.CopyTo(stream);
            } 
        }

        public static void ConvertStreamToValue(Stream stream, DataType dataType, ref object converted)
        {
            switch (dataType)
            {
                case DataType.String:
                    using (StreamReader sr = new StreamReader(stream))
                    {
                        converted = sr.ReadToEnd();
                    }
                    break;
                case DataType.Binary:
                    using (MemoryStream ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        converted = ms.ToArray();
                    }
                    break;
                case DataType.Stream:
                    // when the target value is a Stream, we copy the value
                    // into the Stream passed in
                    Stream targetStream = converted as Stream;
                    stream.CopyTo(targetStream);
                    break;
            }
        }
    }
}
