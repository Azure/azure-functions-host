// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.Description.DotNet
{
    /// <summary>
    /// Represents the runtime graph configuration defined in runtimes.json.
    /// </summary>
    internal sealed class RuntimeGraph
    {
        public Dictionary<string, RuntimeInfo> Runtimes { get; set; } = [];
    }
}
