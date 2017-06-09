// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Host.Queues.Bindings
{
    // Write up bindinging rules for [Queue] attribute. 
    // This is fundemantentally an IAsyncCollector<IStorageQueueMessage>
    internal class QueueExtension : IExtensionConfigProvider
    {      
        public QueueExtension()
        {
        }

        public void Initialize(ExtensionConfigContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var config = new PerHostConfig();
            config.Initialize(context);
        }

        // Multiple JobHost objects may share the same JobHostConfiguration.
        // But queues have per-host instance state (IMessageEnqueuedWatcher). 
        // so capture that and create new binding rules per host instance. 
        private class PerHostConfig
        {
            // Fields that the various binding funcs need to close over. 
            private IStorageAccountProvider _accountProvider;

            // Optimization where a queue output can directly trigger a queue input. 
            // This is per-host (not per-config)
            private ContextAccessor<IMessageEnqueuedWatcher> _messageEnqueuedWatcherGetter;
            
            public void Initialize(ExtensionConfigContext context)
            {
                _messageEnqueuedWatcherGetter = context.PerHostServices.GetService<ContextAccessor<IMessageEnqueuedWatcher>>();
                _accountProvider = context.Config.GetService<IStorageAccountProvider>();

                context.ApplyConfig(context.Config.Queues, "queues");

                // IStorageQueueMessage is the core testing interface 
                var binding = context.AddBindingRule<QueueAttribute>();
                binding
                    .AddConverter<byte[], IStorageQueueMessage>(ConvertByteArrayToCloudQueueMessage)
                    .AddConverter<string, IStorageQueueMessage>(ConvertStringToCloudQueueMessage);

                context   // global converters, apply to multiple attributes. 
                     .AddConverter<IStorageQueueMessage, byte[]>(ConvertCloudQueueMessageToByteArray)
                     .AddConverter<IStorageQueueMessage, string>(ConvertCloudQueueMessageToString)
                     .AddConverter<CloudQueueMessage, IStorageQueueMessage>(ConvertToStorageQueueMessage);

                var builder = new QueueBuilder(this);

                binding.AddValidator(ValidateQueueAttribute)
                    .SetPostResolveHook(ToWriteParameterDescriptorForCollector)
                        .BindToCollector<IStorageQueueMessage>(BuildFromQueueAttribute);

                binding.SetPostResolveHook(ToReadWriteParameterDescriptorForCollector)
                        .BindToInput<IStorageQueue>(builder);

                binding.SetPostResolveHook(ToReadWriteParameterDescriptorForCollector)
                        .BindToInput<CloudQueue>(builder);

                binding.SetPostResolveHook(ToReadWriteParameterDescriptorForCollector)
                        .BindToInput<CloudQueue>(builder);

                IConverterManager converterManager = context.Config.ConverterManager;
                converterManager.AddConverter<object, JObject, QueueAttribute>(SerializeToJobject);
            }

            // Hook JObject serialization to so we can stamp the object with a causality marker. 
            private static JObject SerializeToJobject(object input, QueueAttribute attrResolved, ValueBindingContext context)
            {
                JObject objectToken = JObject.FromObject(input, JsonSerialization.Serializer);
                var functionInstanceId = context.FunctionInstanceId;
                QueueCausalityManager.SetOwner(functionInstanceId, objectToken);

                return objectToken;
            }

            // ParameterDescriptor for binding to CloudQueue. Whereas the output bindings are FileAccess.Write; CloudQueue exposes Peek() 
            // and so is technically Read/Write. 
            // Preserves compat with older SDK. 
            private ParameterDescriptor ToReadWriteParameterDescriptorForCollector(QueueAttribute attr, ParameterInfo parameter, INameResolver nameResolver)
            {
                return ToParameterDescriptorForCollector(attr, parameter, nameResolver, FileAccess.ReadWrite);
            }

            // Asyncollector version. Write-only 
            private ParameterDescriptor ToWriteParameterDescriptorForCollector(QueueAttribute attr, ParameterInfo parameter, INameResolver nameResolver)
            {
                return ToParameterDescriptorForCollector(attr, parameter, nameResolver, FileAccess.Write);
            }

            private ParameterDescriptor ToParameterDescriptorForCollector(QueueAttribute attr, ParameterInfo parameter, INameResolver nameResolver, FileAccess access)
            {
                Task<IStorageAccount> t = Task.Run(() =>
                    _accountProvider.GetStorageAccountAsync(attr, CancellationToken.None, nameResolver));
                IStorageAccount account = t.GetAwaiter().GetResult();

                string accountName = account.Credentials.AccountName;

                return new QueueParameterDescriptor
                {
                    Name = parameter.Name,
                    AccountName = accountName,
                    QueueName = NormalizeQueueName(attr, nameResolver),
                    Access = access
                };
            }

            private static string NormalizeQueueName(QueueAttribute attribute, INameResolver nameResolver)
            {
                string queueName = attribute.QueueName;
                if (nameResolver != null)
                {
                    queueName = nameResolver.ResolveWholeString(queueName);
                }
                queueName = queueName.ToLowerInvariant(); // must be lowercase. coerce here to be nice.
                return queueName;
            }

            // This is a static validation (so only %% are resolved; not {} ) 
            // For runtime validation, the regular builder functions can do the resolution.
            private void ValidateQueueAttribute(QueueAttribute attribute, Type parameterType)
            {
                string queueName = NormalizeQueueName(attribute, null);

                // Queue pre-existing  behavior: if there are { }in the path, then defer validation until runtime. 
                if (!queueName.Contains("{"))
                {
                    QueueClient.ValidateQueueName(queueName);
                }
            }

            private IStorageQueueMessage ConvertToStorageQueueMessage(CloudQueueMessage arg)
            {
                return new StorageQueueMessage(arg);
            }

            private byte[] ConvertCloudQueueMessageToByteArray(IStorageQueueMessage arg)
            {
                return arg.AsBytes;
            }

            private string ConvertCloudQueueMessageToString(IStorageQueueMessage arg)
            {
                return arg.AsString;
            }

            private IStorageQueueMessage ConvertByteArrayToCloudQueueMessage(byte[] arg, QueueAttribute attrResolved)
            {
                IStorageQueue queue = GetQueue(attrResolved);
                var msg = queue.CreateMessage(arg);
                return msg;
            }

            private IStorageQueueMessage ConvertStringToCloudQueueMessage(string arg, QueueAttribute attrResolved)
            {
                IStorageQueue queue = GetQueue(attrResolved);
                var msg = queue.CreateMessage(arg);
                return msg;
            }

            private IAsyncCollector<IStorageQueueMessage> BuildFromQueueAttribute(QueueAttribute attrResolved)
            {
                IStorageQueue queue = GetQueue(attrResolved);
                return new QueueAsyncCollector(queue, _messageEnqueuedWatcherGetter.Value);
            }

            internal IStorageQueue GetQueue(QueueAttribute attrResolved)
            {
                var account = Task.Run(() => _accountProvider.GetStorageAccountAsync(attrResolved, CancellationToken.None)).GetAwaiter().GetResult();
                var client = account.CreateQueueClient();

                string queueName = attrResolved.QueueName.ToLowerInvariant();
                QueueClient.ValidateQueueName(queueName);

                return client.GetQueueReference(queueName);
            }
        }

        private class QueueBuilder : 
            IAsyncConverter<QueueAttribute, IStorageQueue>, 
            IAsyncConverter<QueueAttribute, CloudQueue>
        {
            private readonly PerHostConfig _bindingProvider;

            public QueueBuilder(PerHostConfig bindingProvider)
            {
                _bindingProvider = bindingProvider;
            }

            async Task<IStorageQueue> IAsyncConverter<QueueAttribute, IStorageQueue>.ConvertAsync(
                QueueAttribute attrResolved,
                CancellationToken cancellation)
            {
                IStorageQueue queue = _bindingProvider.GetQueue(attrResolved);
                await queue.CreateIfNotExistsAsync(CancellationToken.None);
                return queue;
            }

            async Task<CloudQueue> IAsyncConverter<QueueAttribute, CloudQueue>.ConvertAsync(
                QueueAttribute attrResolved,
                CancellationToken cancellation)
            {
                IAsyncConverter<QueueAttribute, IStorageQueue> convert = this;
                var queue = await convert.ConvertAsync(attrResolved, cancellation);
                return queue.SdkObject;
            }
        }

        // The core Async Collector for queueing messages. 
        internal class QueueAsyncCollector : IAsyncCollector<IStorageQueueMessage>
        {
            private readonly IStorageQueue _queue;
            private readonly IMessageEnqueuedWatcher _messageEnqueuedWatcher;

            public QueueAsyncCollector(IStorageQueue queue, IMessageEnqueuedWatcher messageEnqueuedWatcher)
            {
                this._queue = queue;
                this._messageEnqueuedWatcher = messageEnqueuedWatcher;
            }

            public async Task AddAsync(IStorageQueueMessage message, CancellationToken cancellationToken = default(CancellationToken))
            {
                if (message == null)
                {
                    throw new InvalidOperationException("Cannot enqueue a null queue message instance.");
                }

                await _queue.AddMessageAndCreateIfNotExistsAsync(message, cancellationToken);

                if (_messageEnqueuedWatcher != null)
                {
                    _messageEnqueuedWatcher.Notify(_queue.Name);
                }
            }

            public Task FlushAsync(CancellationToken cancellationToken = default(CancellationToken))
            {
                // Batching not supported. 
                return Task.FromResult(0);
            }
        }
    }
}