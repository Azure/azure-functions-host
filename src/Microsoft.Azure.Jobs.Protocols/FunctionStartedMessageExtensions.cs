// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Provides extension methods for the <see cref="FunctionStartedMessage"/> class.</summary>
#if PUBLICPROTOCOL
    public static class FunctionStartedMessageExtensions
#else
    internal static class FunctionStartedMessageExtensions
#endif
    {
        /// <summary>Format's a function's <see cref="ExecutionReason"/> in a display-friendly text format.</summary>
        /// <param name="message">The function whose reason to format.</param>
        /// <returns>A function's <see cref="ExecutionReason"/> in a display-friendly text format.</returns>
        public static string FormatReason(this FunctionStartedMessage message)
        {
            if (message == null)
            {
                throw new ArgumentNullException("message");
            }

            ExecutionReason reason = message.Reason;

            switch (reason)
            {
                case ExecutionReason.AutomaticTrigger:
                    return GetAutomaticTriggerReason(message);
                case ExecutionReason.HostCall:
                    return "This was function was programmatically called via the host APIs.";
                case ExecutionReason.Dashboard:
                    return message.ParentId.HasValue ? "Replayed from Dashboard." : "Ran from Dashboard.";
                default:
                    return null;
            }
        }

        private static string GetAutomaticTriggerReason(FunctionStartedMessage message)
        {
            string blobPath = GetArgumentValue<BlobTriggerParameterDescriptor>(message);

            if (blobPath != null)
            {
                return "New blob detected: " + blobPath;
            }

            QueueTriggerParameterDescriptor queueTrigger = GetParameterDescriptor<QueueTriggerParameterDescriptor>(message);

            if (queueTrigger != null)
            {
                return "New queue message detected on '" + queueTrigger.QueueName + "'.";
            }

            ServiceBusTriggerParameterDescriptor serviceBusTrigger = GetParameterDescriptor<ServiceBusTriggerParameterDescriptor>(message);

            if (serviceBusTrigger != null)
            {
                return "New service bus message detected on '" + GetPath(serviceBusTrigger) + "'.";
            }

            return null;
        }

        private static T GetParameterDescriptor<T>(FunctionStartedMessage message) where T : ParameterDescriptor
        {
            return message.Function.Parameters.OfType<T>().FirstOrDefault();
        }

        private static string GetArgumentValue<T>(FunctionStartedMessage message) where T : ParameterDescriptor
        {
            T parameterDescriptor = GetParameterDescriptor<T>(message);

            if (parameterDescriptor == null)
            {
                return null;
            }

            if (!message.Arguments.ContainsKey(parameterDescriptor.Name))
            {
                return null;
            }

            return message.Arguments[parameterDescriptor.Name];
        }

        private static string GetPath(ServiceBusTriggerParameterDescriptor serviceBusTrigger)
        {
            if (serviceBusTrigger.QueueName != null)
            {
                return serviceBusTrigger.QueueName;
            }

            return serviceBusTrigger.TopicName + "/Subscriptions/" + serviceBusTrigger.SubscriptionName;
        }
    }
}
