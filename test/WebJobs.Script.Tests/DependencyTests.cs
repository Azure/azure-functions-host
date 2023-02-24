// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Extensions.DependencyModel;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class DependencyTests
    {
        // These are changed often and controlled by us, so we don't need to fail if they are updated.
        private static readonly string[] _excludedList = new[]
        {
            "Microsoft.Azure.WebJobs.Script.dll",
            "Microsoft.Azure.WebJobs.Script.Grpc.dll",
            "Microsoft.Azure.WebJobs.Script.WebHost.dll",
            "Microsoft.Azure.WebJobs.dll",
            "Microsoft.Azure.WebJobs.Host.dll",
            "Microsoft.Azure.WebJobs.Logging.ApplicationInsights.dll",
            "Microsoft.Azure.WebJobs.Script.Abstractions.dll",
            "Microsoft.Azure.WebJobs.Extensions.dll",
            "Microsoft.Azure.WebJobs.Extensions.Http.dll",
            "Microsoft.Azure.WebJobs.Host.Storage.dll",
            "Microsoft.Azure.WebJobs.Logging.dll",
            "Microsoft.Azure.AppService.Middleware.dll",
            "Microsoft.Azure.AppService.Middleware.Modules.dll",
            "Microsoft.Azure.AppService.Middleware.Functions.dll",
            "Microsoft.Azure.AppService.Middleware.NetCore.dll",
        };

        private readonly DependencyContextJsonReader _reader = new DependencyContextJsonReader();
        private readonly IEnumerable<string> _rids = DependencyHelper.GetRuntimeFallbacks();

        [Fact]
        public void Verify_DepsJsonChanges()
        {
            string depsJsonFileName = "Microsoft.Azure.WebJobs.Script.WebHost.deps.json";
            string oldDepsJson = Path.GetFullPath(depsJsonFileName);
            string webhostBinPath = Path.Combine("..", "..", "..", "..", "..", "src", "WebJobs.Script.WebHost", "bin");
            string newDepsJson = Directory.GetFiles(Path.GetFullPath(webhostBinPath), depsJsonFileName, SearchOption.AllDirectories).FirstOrDefault();

            Assert.True(File.Exists(oldDepsJson), $"{oldDepsJson} not found.");
            Assert.True(File.Exists(newDepsJson), $"{newDepsJson} not found.");

            IEnumerable<RuntimeFile> oldAssets = GetRuntimeFiles(oldDepsJson);
            IEnumerable<RuntimeFile> newAssets = GetRuntimeFiles(newDepsJson);

            var comparer = new RuntimeFileComparer();

            var removed = oldAssets.Except(newAssets, comparer).ToList();
            var added = newAssets.Except(oldAssets, comparer).ToList();

            bool succeed = removed.Count == 0 && added.Count == 0;

            if (succeed)
            {
                return;
            }

            IList<RuntimeFile> changed = new List<RuntimeFile>();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("IMPORTANT: The dependencies in WebHost have changed and MUST be reviewed before proceeding. Please follow up with brettsam, fabiocav or mathewc for approval.");
            sb.AppendLine();
            sb.AppendLine($"Previous file: {oldDepsJson}");
            sb.AppendLine($"New file:      {newDepsJson}");
            sb.AppendLine();
            sb.AppendLine("  Changed:");
            foreach (RuntimeFile oldFile in oldAssets)
            {
                string fileName = Path.GetFileName(oldFile.Path);
                if (_excludedList.Contains(fileName))
                {
                    continue;
                }

                var newFile = newAssets.SingleOrDefault(p =>
                {
                    return Path.GetFileName(p.Path) == fileName &&
                        (p.FileVersion != oldFile.FileVersion ||
                         p.AssemblyVersion != oldFile.AssemblyVersion);
                });

                if (newFile != null)
                {
                    sb.AppendLine($"    - {fileName}: {oldFile.AssemblyVersion}/{oldFile.FileVersion} -> {newFile.AssemblyVersion}/{newFile.FileVersion}");
                    changed.Add(oldFile);
                    changed.Add(newFile);
                }
            }

            sb.AppendLine();
            sb.AppendLine("  Removed:");
            foreach (RuntimeFile f in removed.Except(changed))
            {
                sb.AppendLine($"    - {Path.GetFileName(f.Path)}: {f.AssemblyVersion}/{f.FileVersion}");
            }
            sb.AppendLine();
            sb.AppendLine("  Added:");
            foreach (RuntimeFile f in added.Except(changed))
            {
                sb.AppendLine($"    - {Path.GetFileName(f.Path)}: {f.AssemblyVersion}/{f.FileVersion}");
            }

            Assert.True(succeed, sb.ToString());
        }

        private IEnumerable<RuntimeFile> GetRuntimeFiles(string depsJsonFileName)
        {
            using (Stream s = new FileStream(depsJsonFileName, FileMode.Open))
            {
                DependencyContext deps = _reader.Read(s);

                return deps.RuntimeLibraries
                    .SelectMany(l => SelectRuntimeAssemblyGroup(_rids, l.RuntimeAssemblyGroups))
                    .Where(l => !_excludedList.Contains(Path.GetFileName(l.Path)))
                    .OrderBy(p => Path.GetFileName(p.Path));
            }
        }

        private static IEnumerable<RuntimeFile> SelectRuntimeAssemblyGroup(IEnumerable<string> rids, IReadOnlyList<RuntimeAssetGroup> runtimeAssemblyGroups)
        {
            // Attempt to load group for the current RID graph
            foreach (var rid in rids)
            {
                var assemblyGroup = runtimeAssemblyGroups.FirstOrDefault(g => string.Equals(g.Runtime, rid, StringComparison.OrdinalIgnoreCase));
                if (assemblyGroup != null)
                {
                    return assemblyGroup.RuntimeFiles;
                }
            }

            // If unsuccessful, load default assets, making sure the path is flattened to reflect deployed files
            return runtimeAssemblyGroups.GetDefaultRuntimeFileAssets();
        }

        private class RuntimeFileComparer : IEqualityComparer<RuntimeFile>
        {
            public bool Equals([AllowNull] RuntimeFile x, [AllowNull] RuntimeFile y)
            {
                return x.AssemblyVersion == y.AssemblyVersion &&
                    x.FileVersion == y.FileVersion &&
                    x.Path == y.Path;
            }

            public int GetHashCode([DisallowNull] RuntimeFile obj)
            {
                string code = obj.Path + obj.AssemblyVersion + obj.FileVersion;
                return code.GetHashCode();
            }
        }
    }
}
