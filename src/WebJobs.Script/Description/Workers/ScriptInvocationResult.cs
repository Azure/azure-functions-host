// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.Immutable;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class ScriptInvocationResult
    {
        public static readonly ScriptInvocationResult Success = new ScriptInvocationResult
        {
            Outputs = ImmutableDictionary<string, object>.Empty
        };

        public object Return { get; set; }

        public IDictionary<string, object> Outputs { get; set; }
    }
}
