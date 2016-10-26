// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
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
    internal class QueueBindingProvider
    {
        // Fields that the various binding funcs need to close over. 
        private readonly IStorageAccountProvider _accountProvider;
        private readonly IContextGetter<IMessageEnqueuedWatcher> _messageEnqueuedWatcherGetter;

        // Use the static Build method to create. 
        // Constructor is just for capturing instance fields used in func closures. 
        private QueueBindingProvider(
               IStorageAccountProvider accountProvider,
               IContextGetter<IMessageEnqueuedWatcher> messageEnqueuedWatcherGetter)
        {
            _accountProvider = accountProvider;
            _messageEnqueuedWatcherGetter = messageEnqueuedWatcherGetter;
        }

        public static IBindingProvider Build(
            IStorageAccountProvider accountProvider,
            IContextGetter<IMessageEnqueuedWatcher> messageEnqueuedWatcherGetter,
            INameResolver nameResolver,
            IConverterManager converterManager)
        {
            var closure = new QueueBindingProvider(accountProvider, messageEnqueuedWatcherGetter);
            var bindingProvider = closure.New(nameResolver, converterManager);
            return bindingProvider;
        }

        // Helper to allocate a binding provider for [Queue].
        // private instance method since it's refering to lambdas that close over the instance fields. 
        private IBindingProvider New(
            INameResolver nameResolver,
            IConverterManager converterManager)
        {
            // IStorageQueueMessage is the core testing interface 
            converterManager.AddConverter<byte[], IStorageQueueMessage, QueueAttribute>(ConvertByteArrayToCloudQueueMessage);
            converterManager.AddConverter<IStorageQueueMessage, byte[]>(ConvertCloudQueueMessageToByteArray);

            converterManager.AddConverter<object, JObject, QueueAttribute>(SerializeToJobject);
            converterManager.AddConverter<string, IStorageQueueMessage, QueueAttribute>(ConvertStringToCloudQueueMessage);
            converterManager.AddConverter<IStorageQueueMessage, string>(ConvertCloudQueueMessageToString);

            converterManager.AddConverter<CloudQueueMessage, IStorageQueueMessage>(ConvertToStorageQueueMessage);

            var bindingFactory = new BindingFactory(nameResolver, converterManager);

            var bindAsyncCollector = bindingFactory.BindToAsyncCollector<QueueAttribute, IStorageQueueMessage>(BuildFromQueueAttribute, ToWriteParameterDescriptorForCollector, CollectAttributeInfo);
            var bindClient = bindingFactory.BindToExactAsyncType<QueueAttribute, IStorageQueue>(BuildClientFromQueueAttributeAsync, ToReadWriteParameterDescriptorForCollector, CollectAttributeInfo);
            var bindSdkClient = bindingFactory.BindToExactAsyncType<QueueAttribute, CloudQueue>(BuildRealClientFromQueueAttributeAsync, ToReadWriteParameterDescriptorForCollector, CollectAttributeInfo);
                        
            var bindingProvider = new GenericCompositeBindingProvider<QueueAttribute>(
                ValidateQueueAttribute, nameResolver, bindClient, bindSdkClient, bindAsyncCollector);

            return bindingProvider;
        }

        // Hook JObject serialization to so we can stamp the object with a causality marker. 
        private static JObject SerializeToJobject(object input, QueueAttribute attrResolved, ValueBindingContext context)
        {
            JObject objectToken = JObject.FromObject(input, JsonSerialization.Serializer);
            var functionInstanceId = context.FunctionInstanceId;
            QueueCausalityManager.SetOwner(functionInstanceId, objectToken);

            return objectToken;
        }

        // [Queue] has some pre-existing behavior where the storage account can be specified outside of the [Queue] attribute. 
        // The storage account is pulled from the ParameterInfo (which could pull in a [Storage] attribute on the container class)
        // Resolve everything back down to a single attribute so we can use the binding helpers. 
        // This pattern should be rare since other extensions can just keep everything directly on the primary attribute. 
        private async Task<QueueAttribute> CollectAttributeInfo(QueueAttribute attrResolved, ParameterInfo parameter, INameResolver nameResolver)
        {
            // Look for [Storage] attribute and squirrel over 
            IStorageAccount account = await _accountProvider.GetStorageAccountAsync(parameter, CancellationToken.None, nameResolver);
            StorageClientFactoryContext clientFactoryContext = new StorageClientFactoryContext
            {
                Parameter = parameter
            };
            IStorageQueueClient client = account.CreateQueueClient(clientFactoryContext);
                    
            return new ResolvedQueueAttribute(attrResolved.QueueName, client);
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
                _accountProvider.GetStorageAccountAsync(parameter, CancellationToken.None, nameResolver));
            IStorageAccount account = t.GetAwaiter().GetResult();

            if (account == null)
            {
                throw new InvalidOperationException("Unable to bind Queue because no storage account has been configured.");
            }

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

        private async Task<CloudQueue> BuildRealClientFromQueueAttributeAsync(QueueAttribute attrResolved)
        {
            var queue = await this.BuildClientFromQueueAttributeAsync(attrResolved);
            return queue.SdkObject;
        }

        private async Task<IStorageQueue> BuildClientFromQueueAttributeAsync(QueueAttribute attrResolved)
        {
            IStorageQueue queue = GetQueue(attrResolved);
            await queue.CreateIfNotExistsAsync(CancellationToken.None);
            return queue;
        }

        private IAsyncCollector<IStorageQueueMessage> BuildFromQueueAttribute(QueueAttribute attrResolved)
        {
            IStorageQueue queue = GetQueue(attrResolved);
            return new QueueAsyncCollector(queue, _messageEnqueuedWatcherGetter.Value);
        }

        private static IStorageQueue GetQueue(QueueAttribute attrResolved)
        {
            var attr = (ResolvedQueueAttribute)attrResolved;
            IStorageQueue queue = attr.GetQueue();
            return queue;
        }

        // Queue attributes can optionally be paired with a separate [StorageAccount]. 
        // Consolidate the information from both attributes into a single attribute.
        // New extensions should just place everything in the attribute or the configuration and so shouldn't need to do this. 
        internal sealed class ResolvedQueueAttribute : QueueAttribute
        {
            public ResolvedQueueAttribute(string queueName, IStorageQueueClient client)
                : base(queueName)
            {
                this.Client = client;
            }

            internal IStorageQueueClient Client { get; private set; }

            public IStorageQueue GetQueue()
            {
                // Azure Queues must be lowercase. 
                // pre-existing behavior: coerce name to lowercase to be nice. 
                string queueName = this.QueueName.ToLowerInvariant();

                QueueClient.ValidateQueueName(queueName);

                return this.Client.GetQueueReference(queueName);
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