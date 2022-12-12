// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.Extensions
{
    public static class BindingMetadataExtensions
    {
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
