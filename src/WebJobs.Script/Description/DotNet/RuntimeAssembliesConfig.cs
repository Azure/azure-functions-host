// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    /// <summary>
    /// Represents the configuration that lists runtime assemblies and their resolution policies.
    /// </summary>
    internal sealed class RuntimeAssembliesConfig
    {
        public List<ScriptRuntimeAssembly> RuntimeAssemblies { get; set; }
    }
}
