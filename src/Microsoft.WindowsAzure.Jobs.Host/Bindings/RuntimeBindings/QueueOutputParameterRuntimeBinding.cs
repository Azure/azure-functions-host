using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.WindowsAzure.StorageClient;

namespace Microsoft.WindowsAzure.Jobs
{
    // On output, the object payload gets queued
    internal class QueueOutputParameterRuntimeBinding : ParameterRuntimeBinding
    {
        public CloudQueueDescriptor QueueOutput { get; set; }

        public override BindResult Bind(IConfiguration config, IBinderEx bindingContext, ParameterInfo targetParameter)
        {
            if (!targetParameter.IsOut)
            {
                var msg = string.Format("[QueueOutput] is valid only on 'out' parameters. Can't use on '{0}'.", targetParameter);
                throw new InvalidOperationException(msg);
            }

            CloudQueue queue = this.QueueOutput.GetQueue();
            Guid functionInstance = bindingContext.FunctionInstanceGuid;
            IQueueResultConverter converter;

            Type parameterType = targetParameter.ParameterType;
            if (parameterType.IsByRef)
            {
                // Unwrap the parameter type if it's Type&
                parameterType = parameterType.GetElementType();
            }

            // Special-case IEnumerable<T> to do multiple enqueue
            if (parameterType.IsGenericType && parameterType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                Type elementType = GetCollectionElementType(parameterType);
                IQueueResultConverter itemConverter = CreateElementConverter(elementType);
                converter = new EnumerableQueueResultConverter(itemConverter);
            }
            else
            {
                converter = CreateElementConverter(parameterType);
            }

            return new QueueResult(queue, converter, functionInstance);
        }

        public override string ConvertToInvokeString()
        {
            return QueueOutput.QueueName;
        }

        public override string ToString()
        {
            return string.Format("Output to queue: {0}", QueueOutput.QueueName);
        }

        // Make sure the expected type is used for a single message payload.
        private static void CheckElementType(Type elementType)
        {
            if (elementType == typeof(object))
            {
                throw new InvalidOperationException(string.Format("[QueueOutput] cannot be used on type '{0}'.", typeof(object).Name));
            }

            if (elementType == typeof(string) || elementType == typeof(byte[]))
            {
                // string and byte[] are IEnumerable, but that's OK.
                return;
            }

            if (typeof(IEnumerable).IsAssignableFrom(elementType) && (!elementType.IsGenericType || elementType.GetGenericTypeDefinition() != typeof(IEnumerable<>)))
            {
                throw new InvalidOperationException(string.Format("[QueueOutput] cannot be used on the collection type '{0}'. Use IEnumerable<T> for collection parameters.", elementType.Name));
            }
        }

        private static IQueueResultConverter CreateElementConverter(Type elementType)
        {
            CheckElementType(elementType);

            if (elementType == typeof(string))
            {
                return new StringQueueResultConverter();
            }
            else if (elementType == typeof(byte[]))
            {
                return new ByteArrayQueueResultConverter();
            }
            else
            {
                return new JsonQueueResultConverter();
            }
        }

        // Make sure the expected type is used for multiple message payloads.
        // Here we can assume that the collectionType is IEnumerable<T>
        private static Type GetCollectionElementType(Type collectionType)
        {
            Type elementType = collectionType.GetGenericArguments()[0];

            // elementType cannot be another IEnumerable<T>
            if (elementType.IsGenericType && elementType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                throw new InvalidOperationException("Nested IEnumerable<T> is not supported.");
            }

            return elementType;
        }

        // Defines a sink for queue messages.
        private interface IQueueMessageCollector
        {
            void Add(CloudQueueMessage message);
        }

        // Defines a converter that takes a result and produces one or more enqueued messages.
        private interface IQueueResultConverter
        {
            void Convert(object result, Guid functionInstance, IQueueMessageCollector collector);
        }

        private class ByteArrayQueueResultConverter : IQueueResultConverter
        {
            public void Convert(object result, Guid functionInstance, IQueueMessageCollector collector)
            {
                byte[] content = result as byte[];

                if (content != null)
                {
                    collector.Add(new CloudQueueMessage(content));
                }
            }
        }

        private class EnumerableQueueResultConverter : IQueueResultConverter
        {
            private readonly IQueueResultConverter _itemConverter;

            public EnumerableQueueResultConverter(IQueueResultConverter itemConverter)
            {
                _itemConverter = itemConverter;
            }

            public void Convert(object result, Guid functionInstance, IQueueMessageCollector collector)
            {
                if (result != null)
                {
                    IEnumerable enumerable = (IEnumerable)result;

                    foreach (object item in enumerable)
                    {
                        _itemConverter.Convert(item, functionInstance, collector);
                    }
                }
            }
        }

        private class JsonQueueResultConverter : IQueueResultConverter
        {
            public void Convert(object result, Guid functionInstance, IQueueMessageCollector collector)
            {
                if (result != null)
                {
                    QueueCausalityHelper causality = new QueueCausalityHelper();
                    CloudQueueMessage message = causality.EncodePayload(functionInstance, result);

                    // Beware, as soon as this is added,
                    // another worker can pick up the message and start running.
                    collector.Add(message);
                }
            }
        }

        private class StringQueueResultConverter : IQueueResultConverter
        {
            public void Convert(object result, Guid functionInstance, IQueueMessageCollector collector)
            {
                string content = result as string;

                if (content != null)
                {
                    collector.Add(new CloudQueueMessage(content));
                }
            }
        }

        private class QueueMessageCollector : IQueueMessageCollector
        {
            private readonly CloudQueue _queue;

            public QueueMessageCollector(CloudQueue queue)
            {
                _queue = queue;
            }

            public void Add(CloudQueueMessage message)
            {
                _queue.AddMessage(message);
            }
        }

        private class QueueResult : BindResult
        {
            private readonly Guid _functionInstance;
            private readonly IQueueMessageCollector _collector;
            private readonly IQueueResultConverter _converter;

            public QueueResult(CloudQueue queue, IQueueResultConverter converter, Guid functionInstance)
            {
                _collector = new QueueMessageCollector(queue);
                _converter = converter;
                _functionInstance = functionInstance;
                PostActionOrder = PostActionOrder.QueueOutput;
            }

            public override void OnPostAction()
            {
                _converter.Convert(Result, _functionInstance, _collector);
            }
        }
    }
}
