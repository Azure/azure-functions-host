// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.WebJobs.Protocols
#else
namespace Microsoft.Azure.WebJobs.Host.Protocols
#endif
{
    /// <summary>Provides extension methods for the <see cref="FunctionStartedMessage"/> class.</summary>
#if PUBLICPROTOCOL
    public static class FunctionStartedMessageExtensions
#else
    internal static class FunctionStartedMessageExtensions
#endif
    {
        /// <summary>Formats a function's <see cref="ExecutionReason"/> in a display-friendly text format.</summary>
        /// <param name="message">The function whose reason to format.</param>
        /// <returns>A function's <see cref="ExecutionReason"/> in a display-friendly text format.</returns>
        public static string FormatReason(this FunctionStartedMessage message)
        {
            if (message == null)
            {
                throw new ArgumentNullException("message");
            }

            ExecutionReason reason = message.Reason;

            // If the message already contains details use them. This will be the case for
            // messages serialized from the Host to the Dashboard. The host will format the
            // reason before sending
            if (!string.IsNullOrEmpty(message.ReasonDetails))
            {
                return message.ReasonDetails;
            }

            switch (reason)
            {
                case ExecutionReason.AutomaticTrigger:
                    return GetAutomaticTriggerReason(message);
                case ExecutionReason.HostCall:
                    return "This function was programmatically called via the host APIs.";
                case ExecutionReason.Dashboard:
                    return message.ParentId.HasValue ? "Replayed from Dashboard." : "Ran from Dashboard.";
                case ExecutionReason.Portal:
                    return message.ParentId.HasValue ? "Replayed from Portal." : "Ran from Portal.";
                default:
                    return null;
            }
        }

        private static string GetAutomaticTriggerReason(FunctionStartedMessage message)
        {
            TriggerParameterDescriptor triggerParameterDescriptor = GetParameterDescriptor<TriggerParameterDescriptor>(message);
            if (triggerParameterDescriptor != null)
            {
                return triggerParameterDescriptor.GetTriggerReason(message.Arguments);
            }

            return null;
        }

        private static T GetParameterDescriptor<T>(FunctionStartedMessage message) where T : ParameterDescriptor
        {
            return message.Function.Parameters.OfType<T>().FirstOrDefault();
        }
    }
}
