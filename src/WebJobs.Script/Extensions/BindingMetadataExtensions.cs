// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.Extensions
{
    public static class BindingMetadataExtensions
    {
        public static bool SupportsDeferredBinding(this BindingMetadata metadata) => GetBoolProperty(metadata.Properties, ScriptConstants.SupportsDeferredBindingKey);

        public static bool SkipDeferredBinding(this BindingMetadata metadata) => GetBoolProperty(metadata.Properties, ScriptConstants.SkipDeferredBindingKey);

        private static bool GetBoolProperty(IDictionary<string, object> properties, string propertyKey)
        {
            return properties.TryGetValue(propertyKey, out object valObj)
                && valObj is bool valBool
                && valBool;
        }
    }
}
