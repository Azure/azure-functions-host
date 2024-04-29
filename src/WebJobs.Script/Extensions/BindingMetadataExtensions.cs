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
        private const string HttpTrigger = "httpTrigger";
        private const string EventGridTrigger = "eventGridTrigger";
        private const string SignalRTrigger = "signalRTrigger";
        private const string BlobTrigger = "blobTrigger";

        private const string BlobSourceKey = "source";
        private const string EventGridSource = "eventGrid";

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

            return string.Equals(HttpTrigger, binding.Type, StringComparison.OrdinalIgnoreCase);
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

            if (string.Equals(EventGridTrigger, binding.Type, StringComparison.OrdinalIgnoreCase)
                || string.Equals(SignalRTrigger, binding.Type, StringComparison.OrdinalIgnoreCase))
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

            if (string.Equals(BlobTrigger, binding.Type, StringComparison.OrdinalIgnoreCase))
            {
                if (binding.Raw is { } obj)
                {
                    if (obj.TryGetValue(BlobSourceKey, StringComparison.OrdinalIgnoreCase, out JToken token) && token is not null)
                    {
                        return string.Equals(token.ToString(), EventGridSource, StringComparison.OrdinalIgnoreCase);
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
