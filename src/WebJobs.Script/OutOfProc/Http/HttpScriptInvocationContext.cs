// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.OutOfProc.Http
{
    public class HttpScriptInvocationContext
    {
        public IDictionary<string, object> Data { get; set; } = new Dictionary<string, object>();

        public IDictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }
}
