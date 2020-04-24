// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Azure.WebJobs.Script.Abstractions.Description;

namespace Microsoft.Azure.WebJobs.Script
{
    public static class FunctionMetadataExtensions
    {
        private const string IsDirectKey = "IsDirect";
        private const string IsDisabledKey = "IsDisabled";
        private const string IsCodelessKey = "IsCodeless";

        public static bool IsProxy(this FunctionMetadata metadata) =>
            metadata is ProxyFunctionMetadata;

        public static bool IsDirect(this FunctionMetadata metadata) =>
            GetBoolProperty(metadata.Properties, IsDirectKey);

        public static bool IsDisabled(this FunctionMetadata metadata) =>
            GetBoolProperty(metadata.Properties, IsDisabledKey);

        public static bool IsCodeless(this FunctionMetadata metadata) =>
            GetBoolProperty(metadata.Properties, IsCodelessKey);

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

        private static bool GetBoolProperty(IDictionary<string, object> properties, string propertyKey)
        {
            return properties.TryGetValue(propertyKey, out object valObj)
                && valObj is bool valBool
                && valBool;
        }
    }
}
