// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    [DebuggerDisplay("{" + nameof(Display) + ",nq}")]
    public class RuntimeAsset
    {
        public RuntimeAsset(string rid, string path)
        {
            Rid = rid;
            Path = path;
        }

        public string Rid { get; }

        public string Path { get; }

        private string Display => $"({Rid ?? "no RID"}) - {Path}";
    }
}
