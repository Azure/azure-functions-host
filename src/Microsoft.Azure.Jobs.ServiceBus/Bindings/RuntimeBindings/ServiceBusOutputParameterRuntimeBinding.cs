using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;
using Newtonsoft.Json;

namespace Microsoft.Azure.Jobs
{
    internal class ServiceBusOutputParameterRuntimeBinding : ParameterRuntimeBinding
    {
        [JsonIgnore]
        public string ServiceBusConnectionString { get; set; }

        public string EntityPath { get; set; }

        public override BindResult Bind(IConfiguration config, IBinderEx bindingContext, ParameterInfo targetParameter)
        {
            if (!targetParameter.IsOut)
            {
                var msg = string.Format("[ServiceBusOutput] is valid only on 'out' parameters. Can't use on '{0}'.", targetParameter);
                throw new InvalidOperationException(msg);
            }

            Guid functionInstance = bindingContext.FunctionInstanceGuid;
            IServiceBusResultConverter converter;

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
                IServiceBusResultConverter itemConverter = CreateElementConverter(elementType);
                converter = new EnumerableServiceBusResultConverter(itemConverter);
            }
            else
            {
                converter = CreateElementConverter(parameterType);
            }

            return new ServiceBusResult(EntityPath, ServiceBusConnectionString, converter, functionInstance);
        }

        public override string ConvertToInvokeString()
        {
            return EntityPath;
        }

        public override string ToString()
        {
            return string.Format("Output to ServiceBus: '{0}'", EntityPath);
        }

        // Make sure the expected type is used for a single message payload.
        private static void CheckElementType(Type elementType)
        {
            if (elementType == typeof(object))
            {
                throw new InvalidOperationException(string.Format("[ServiceBus] cannot be used on type '{0}'.", typeof(object).Name));
            }

            if (elementType == typeof(string) || elementType == typeof(byte[]))
            {
                // string and byte[] are IEnumerable, but that's OK.
                return;
            }

            if (typeof(IEnumerable).IsAssignableFrom(elementType) && (!elementType.IsGenericType || elementType.GetGenericTypeDefinition() != typeof(IEnumerable<>)))
            {
                throw new InvalidOperationException(string.Format("[ServiceBus] cannot be used on the collection type '{0}'. Use IEnumerable<T> for collection parameters.", elementType.Name));
            }
        }

        private static IServiceBusResultConverter CreateElementConverter(Type elementType)
        {
            CheckElementType(elementType);

            if (elementType == typeof(string))
            {
                return new StringServiceBusResultConverter();
            }
            else if (elementType == typeof(byte[]))
            {
                return new ByteArrayServiceBusResultConverter();
            }
            else if (elementType == typeof(BrokeredMessage))
            {
                return new BrokeredMessageServiceBusResultConverter();
            }
            else
            {
                return new JsonServiceBusResultConverter();
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
        private interface IBrokeredMessageCollector
        {
            void Add(BrokeredMessage message);
        }

        // Defines a converter that takes a result and produces one or more enqueued messages.
        private interface IServiceBusResultConverter
        {
            void Convert(object result, Guid functionInstance, IBrokeredMessageCollector collector);
        }

        private class BrokeredMessageServiceBusResultConverter : IServiceBusResultConverter
        {
            public virtual void Convert(object result, Guid functionInstance, IBrokeredMessageCollector collector)
            {
                var msg = (BrokeredMessage) result;
                var causality = new ServiceBusCausalityHelper();
                causality.EncodePayload(functionInstance, msg);
                collector.Add(msg);
            }
        }

        private class ByteArrayServiceBusResultConverter : BrokeredMessageServiceBusResultConverter
        {
            public override void Convert(object result, Guid functionInstance, IBrokeredMessageCollector collector)
            {
                byte[] content = result as byte[];

                if (content != null)
                {
                    var msg = new BrokeredMessage(new MemoryStream(content, false));
                    base.Convert(msg, functionInstance, collector);
                }
            }
        }

        private class StringServiceBusResultConverter : BrokeredMessageServiceBusResultConverter
        {
            public override void Convert(object result, Guid functionInstance, IBrokeredMessageCollector collector)
            {
                string content = result as string;

                if (content != null)
                {
                    var msg = new BrokeredMessage(new MemoryStream(Encoding.UTF8.GetBytes(content)));
                    base.Convert(msg, functionInstance, collector);
                }
            }
        }

        private class JsonServiceBusResultConverter : StringServiceBusResultConverter
        {
            public override void Convert(object result, Guid functionInstance, IBrokeredMessageCollector collector)
            {
                if (result!= null)
                {
                    base.Convert(JsonCustom.SerializeObject(result), functionInstance, collector);
                }
            }
        }

        private class EnumerableServiceBusResultConverter : IServiceBusResultConverter
        {
            private readonly IServiceBusResultConverter _itemConverter;

            public EnumerableServiceBusResultConverter(IServiceBusResultConverter itemConverter)
            {
                _itemConverter = itemConverter;
            }

            public void Convert(object result, Guid functionInstance, IBrokeredMessageCollector collector)
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

        private class BrokeredMessageCollector : IBrokeredMessageCollector
        {
            private readonly MessageSender _sender;
            private readonly string _connectionString;

            public BrokeredMessageCollector(MessageSender sender, string connectionString)
            {
                _sender = sender;
                _connectionString = connectionString;
            }

            public void Add(BrokeredMessage message)
            {
                try
                {
                    _sender.Send(message);
                }
                catch (MessagingEntityNotFoundException)
                {
                    try
                    {
                        NamespaceManager.CreateFromConnectionString(_connectionString).CreateQueue(_sender.Path);
                    }
                    catch (MessagingEntityAlreadyExistsException)
                    {
                    }
                    _sender.Send(message);
                }
            }
        }

        private class ServiceBusResult : BindResult
        {
            private readonly Guid _functionInstance;
            private readonly IBrokeredMessageCollector _collector;
            private readonly IServiceBusResultConverter _converter;

            public ServiceBusResult(string senderPath, string connectionString, IServiceBusResultConverter converter, Guid functionInstance)
            {
                var sender =
                    MessagingFactory.CreateFromConnectionString(connectionString).CreateMessageSender(senderPath);
                _collector = new BrokeredMessageCollector(sender, connectionString);
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