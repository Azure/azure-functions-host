// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Extensions
{
    public static class EntityTagHeaderValueExtensions
    {
        public static System.Net.Http.Headers.EntityTagHeaderValue ToSystemETag(this Microsoft.Net.Http.Headers.EntityTagHeaderValue value)
        {
            return value.Tag.Value.StartsWith("\"") && value.Tag.Value.EndsWith("\"")
                ? new System.Net.Http.Headers.EntityTagHeaderValue(value.Tag.Value, value.IsWeak)
                : new System.Net.Http.Headers.EntityTagHeaderValue($"\"{value.Tag}\"", value.IsWeak);
        }
    }
}
