// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Description;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Extensions
{
    public static class BindingMetadataExtensions
    {
        private const string HttpTriggerKey = "httpTrigger";
        private const string EventGridTriggerKey = "eventGridTrigger";
        private const string SignalRTriggerKey = "signalRTrigger";
        private const string BlobTriggerKey = "blobTrigger";

        private static readonly HashSet<string> DurableTriggers = new(StringComparer.OrdinalIgnoreCase)
        {
            "entityTrigger",
            "activityTrigger",
            "orchestrationTrigger"
        };

        /// <summary>
        /// Checks if a <see cref="BindingMetadata"/> represents an HTTP trigger.
        /// </summary>
        /// <param name="binding">The binding metadata to check.</param>
        /// <returns><c>true</c> if an HTTP trigger, <c>false</c> otherwise.</returns>
        public static bool IsHttpTrigger(this BindingMetadata binding)
        {
            if (binding is null)
            {
                throw new ArgumentNullException(nameof(binding));
            }

            return string.Equals(HttpTriggerKey, binding.Type, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Checks if a <see cref="BindingMetadata"/> represents an webhook trigger.
        /// </summary>
        /// <param name="binding">The binding metadata to check.</param>
        /// <returns><c>true</c> if a webhook trigger, <c>false</c> otherwise.</returns>
        /// <remarks>
        /// Known webhook triggers includes SignalR, Event Grid triggers.
        /// </remarks>
        public static bool IsWebHookTrigger(this BindingMetadata binding)
        {
            if (binding is null)
            {
                throw new ArgumentNullException(nameof(binding));
            }

            if (string.Equals(EventGridTriggerKey, binding.Type, StringComparison.OrdinalIgnoreCase)
                || string.Equals(SignalRTriggerKey, binding.Type, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Checks if a <see cref="BindingMetadata"/> represents a durable trigger (entity, orchestration, or activity).
        /// </summary>
        /// <param name="binding">The binding metadata to check.</param>
        /// <returns><c>true</c> if a durable trigger, <c>false</c> otherwise.</returns>
        public static bool IsDurableTrigger(this BindingMetadata binding)
        {
            if (binding is null)
            {
                throw new ArgumentNullException(nameof(binding));
            }

            return DurableTriggers.Contains(binding.Type);
        }

        /// <summary>
        /// Checks if a <see cref="BindingMetadata"/> represents an EventGrid sourced blob trigger.
        /// </summary>
        /// <param name="binding">The binding metadata to check.</param>
        /// <returns><c>true</c> if a EventGrid sourced blob trigger, <c>false</c> otherwise.</returns>
        public static bool IsEventGridBlobTrigger(this BindingMetadata binding)
        {
            if (binding is null)
            {
                throw new ArgumentNullException(nameof(binding));
            }

            if (string.Equals(BlobTriggerKey, binding.Type, StringComparison.OrdinalIgnoreCase))
            {
                if (binding.Raw is { } obj)
                {
                    if (obj.TryGetValue("source", StringComparison.OrdinalIgnoreCase, out JToken token) && token is not null)
                    {
                        return string.Equals(token.ToString(), "eventGrid", StringComparison.OrdinalIgnoreCase);
                    }
                }
            }

            return false;
        }

        public static bool SupportsDeferredBinding(this BindingMetadata metadata)
        {
            Utility.TryReadAsBool(metadata.Properties, ScriptConstants.SupportsDeferredBindingKey, out bool result);
            return result;
        }

        public static bool SkipDeferredBinding(this BindingMetadata metadata)
        {
            Utility.TryReadAsBool(metadata.Properties, ScriptConstants.SkipDeferredBindingKey, out bool result);
            return result;
        }
    }
}
