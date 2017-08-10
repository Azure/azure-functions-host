// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.Description.Script
{
    internal class ScriptInvocationContext
    {
        public ExecutionContext ExecutionContext { get; set; }

        public IEnumerable<(string name, DataType type, object val)> Inputs { get; set; }

        public Dictionary<string, object> BindingData { get; set; }
    }
}
