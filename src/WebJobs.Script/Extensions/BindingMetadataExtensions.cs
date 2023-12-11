// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.Extensions
{
    public static class BindingMetadataExtensions
    {
        private const string HttpTriggerKey = "HttpTrigger";

        private static readonly HashSet<string> DurableTriggers = new(StringComparer.OrdinalIgnoreCase)
        {
            "EntityTrigger",
            "ActivityTrigger",
            "OrchestrationTrigger"
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
