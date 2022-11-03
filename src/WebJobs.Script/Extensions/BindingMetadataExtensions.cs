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
        public static bool SupportsDeferredBinding(this BindingMetadata metadata) => BoolUtility.TryReadAsBool(metadata.Properties, ScriptConstants.SupportsDeferredBindingKey);

        public static bool SkipDeferredBinding(this BindingMetadata metadata) => BoolUtility.TryReadAsBool(metadata.Properties, ScriptConstants.SkipDeferredBindingKey);
    }
}
