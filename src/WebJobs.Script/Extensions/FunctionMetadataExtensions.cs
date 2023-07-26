// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Description;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script
{
    public static class FunctionMetadataExtensions
    {
        private const string IsDirectKey = "IsDirect";
        private const string IsDisabledKey = "IsDisabled";
        private const string IsCodelessKey = "IsCodeless";
        private const string FunctionIdKey = "FunctionId";
        private const string HttpTriggerKey = "HttpTrigger";
        private const string HttpOutputKey = "Http";
        private const string BlobTriggerType = "blobTrigger";
        private const string BlobSourceKey = "source";
        private const string BlobEventGridSourceValue = "EventGrid";

        public static bool IsHttpInAndOutFunction(this FunctionMetadata metadata)
        {
            if (metadata.InputBindings.Count() != 1 || metadata.OutputBindings.Count() != 1)
            {
                return false;
            }

            BindingMetadata inputBindingMetadata = metadata.InputBindings.ElementAt(0);
            BindingMetadata outputBindingMetadata = metadata.OutputBindings.ElementAt(0);
            if (string.Equals(HttpTriggerKey, inputBindingMetadata.Type, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(HttpOutputKey, outputBindingMetadata.Type, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        public static bool IsHttpTriggerFunction(this FunctionMetadata metadata)
        {
            return metadata.InputBindings.Any(b => string.Equals(HttpTriggerKey, b.Type, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsLegacyBlobTriggerFunction(this FunctionMetadata metadata)
        {
            if (metadata.Trigger != null && string.Equals(metadata.Trigger.Type, BlobTriggerType, StringComparison.OrdinalIgnoreCase))
            {
                if (metadata.Trigger.Raw != null)
                {
                    if (metadata.Trigger.Raw.TryGetValue(BlobSourceKey, StringComparison.OrdinalIgnoreCase, out JToken token) && token != null)
                    {
                        return !string.Equals(token.ToString(), BlobEventGridSourceValue, StringComparison.OrdinalIgnoreCase);
                    }
                }
                return true;
            }
            return false;
        }

        public static string GetFunctionId(this FunctionMetadata metadata)
        {
            if (!metadata.Properties.TryGetValue(FunctionIdKey, out object idObj)
                || !(idObj is string))
            {
                metadata.Properties[FunctionIdKey] = Guid.NewGuid().ToString();
            }

            return metadata.Properties[FunctionIdKey] as string;
        }

        internal static void SetFunctionId(this FunctionMetadata metadata, string functionId)
        {
            metadata.Properties[FunctionIdKey] = functionId;
        }

        public static bool IsProxy(this FunctionMetadata metadata) =>
            metadata is ProxyFunctionMetadata;

        public static bool IsDirect(this FunctionMetadata metadata)
        {
            Utility.TryReadAsBool(metadata.Properties, IsDirectKey, out bool result);
            return result;
        }

        public static bool IsDisabled(this FunctionMetadata metadata)
        {
            Utility.TryReadAsBool(metadata.Properties, IsDisabledKey, out bool result);
            return result;
        }

        public static bool IsCodeless(this FunctionMetadata metadata)
        {
            Utility.TryReadAsBool(metadata.Properties, IsCodelessKey, out bool result);
            return result;
        }

        public static bool IsCodelessSet(this FunctionMetadata metadata) =>
            metadata.Properties.ContainsKey(IsCodelessKey);

        /// <summary>
        /// Sets a property indicating whether that this function is a direct invoke.
        /// </summary>
        public static void SetIsDirect(this FunctionMetadata metadata, bool value) =>
            metadata.Properties[IsDirectKey] = value;

        /// <summary>
        /// Sets a property indicating whether the function is disabled.
        /// <remarks>
        /// A disabled function is still compiled and loaded into the host, but it will not
        /// be triggered automatically, and is not publicly addressable (except via admin invoke requests).
        /// </remarks>
        /// </summary>
        public static void SetIsDisabled(this FunctionMetadata metadata, bool value) =>
            metadata.Properties[IsDisabledKey] = value;

        /// <summary>
        /// Sets a property indicating whether this function should be treated as a codeless function.
        /// <remarks>
        /// Codeless function by default will be excluded when quering functions from List API calls
        /// </remarks>
        /// </summary>
        public static void SetIsCodeless(this FunctionMetadata metadata, bool value) =>
            metadata.Properties[IsCodelessKey] = value;
    }
}
