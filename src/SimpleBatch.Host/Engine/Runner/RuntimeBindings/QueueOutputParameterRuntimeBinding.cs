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

        private abstract class QueueBaseResult : BindResult
        {
            public CloudQueue Queue;
            public Guid thisFunction;

            protected virtual void AddMessage(object result)
            {
                if (Result != null)
                {
                    QueueCausalityHelper qcm = new QueueCausalityHelper();
                    CloudQueueMessage msg = qcm.EncodePayload(thisFunction, result);

                    // Beware, as soon as this is added,
                    // another worker can pick up the message and start running.
                    this.Queue.AddMessage(msg);
                }
            }
        }

        // Queues a single message.
        private class QueueResult : QueueBaseResult
        {
            public override void OnPostAction()
            {
                AddMessage(Result);
            }
        }

        // Queues multiple messages.
        private class QueueCollectionResult : QueueBaseResult
        {
            public override void OnPostAction()
            {
                if (Result != null)
                {
                    IEnumerable collection = (IEnumerable)Result;
                    foreach (var value in collection)
                    {
                        AddMessage(value);
                    }
                }
            }
        }

        public override BindResult Bind(IConfiguration config, IBinderEx bindingContext, ParameterInfo targetParameter)
        {
            if (!targetParameter.IsOut)
            {
                var msg = string.Format("[QueueOutput] is only valid on 'out' parameters. Can't use on '{0}'", targetParameter);
                throw new InvalidOperationException(msg);
            }

            Type parameterType = targetParameter.ParameterType;
            if (parameterType.IsByRef)
            {
                // Unwrap the parameter type if it's Type&
                parameterType = parameterType.GetElementType();
            }

            // Special-case IEnumerable<T> to do multiple enqueue
            if (parameterType.IsGenericType && parameterType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                CheckCollectionType(parameterType);
                return new QueueCollectionResult
                {
                    thisFunction = bindingContext.FunctionInstanceGuid,
                    Queue = this.QueueOutput.GetQueue()
                };
            }

            CheckElementType(parameterType);
            return new QueueResult
            {
                thisFunction = bindingContext.FunctionInstanceGuid,
                Queue = this.QueueOutput.GetQueue()
            };
        }

        // Make sure the expected type is used for multiple message payloads.
        // Here we can assume that the collectionType is IEnumerable<T>
        private void CheckCollectionType(Type collectionType)
        {
            Type elementType = collectionType.GetGenericArguments()[0];

            // elementType cannot be another IEnumerable<T>
            if (elementType.IsGenericType && elementType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                throw new InvalidOperationException("Nested IEnumerable<T> is not supported.");
            }

            CheckElementType(elementType);
        }

        // Make sure the expected type is used for a single message payload.
        private void CheckElementType(Type elementType)
        {
            if (elementType == typeof(object))
            {
                throw new InvalidOperationException(string.Format("[QueueOutput] cannot be used on type '{0}'.", typeof(object).Name));
            }

            if (typeof(IEnumerable).IsAssignableFrom(elementType) && (!elementType.IsGenericType || elementType.GetGenericTypeDefinition() != typeof(IEnumerable<>)))
            {
                throw new InvalidOperationException(string.Format("[QueueOutput] cannot be used on the collection type '{0}'. Use IEnumerable<T> for collection parameters.", elementType.Name));
            }
        }

        public override string ConvertToInvokeString()
        {
            return "[set on output]"; // ignored for output parameters anyways.
        }

        public override string ToString()
        {
            return string.Format("Output to queue: {0}", QueueOutput.QueueName);
        }
    }
}
