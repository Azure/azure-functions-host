// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    [DebuggerDisplay("{" + nameof(Display) + ",nq}")]
    public class RuntimeAsset
    {
        public RuntimeAsset(string rid, string path, string assemblyVersion)
        {
            Rid = rid;
            Path = path;

            if (!string.IsNullOrEmpty(assemblyVersion))
            {
                AssemblyVersion = new Version(assemblyVersion);
            }
        }

        public string Rid { get; }

        public string Path { get; }

        public Version AssemblyVersion { get; }

        private string Display => $"({Rid ?? "no RID"}) - {Path} - {AssemblyVersion}";
    }
}
