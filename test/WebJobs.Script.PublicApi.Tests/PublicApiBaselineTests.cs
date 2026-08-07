// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.PublicApi.Tests;

/// <summary>
/// Compares the checked-in compiled public API baselines with the current Release build output.
/// </summary>
/// <remarks>
/// These tests only compare and fail. They never write a checked-in baseline. The explicit
/// <c>eng/script/update-public-api-baselines.ps1</c> workflow writes candidates under
/// <c>out/public-api-candidates</c> and copies them into the repository after verification.
/// </remarks>
public class PublicApiBaselineTests
{
    /// <summary>
    /// When set to <c>1</c>, <see cref="WriteCandidateBaselines"/> writes candidates under <c>out</c>.
    /// </summary>
    public const string UpdateEnvironmentVariable = "UPDATE_PUBLIC_API_BASELINES";

    /// <summary>
    /// Overrides the directory the gate compares against, used to prove failure without touching tracked files.
    /// </summary>
    public const string BaselineDirectoryEnvironmentVariable = "PUBLIC_API_BASELINE_DIRECTORY";

    /// <summary>
    /// The repository-relative directory that receives generated candidate baselines.
    /// </summary>
    public const string CandidateDirectory = "out/public-api-candidates";

    [Fact]
    public void BaselinesMatchCompiledPublicApi()
    {
        var failures = new StringBuilder();

        foreach (CompiledAssembly assembly in CompiledPublicApi.Assemblies)
        {
            string baselinePath = ResolveBaselinePath(assembly.Assembly);

            if (!File.Exists(baselinePath))
            {
                failures
                    .AppendLine($"The baseline '{assembly.Assembly.BaselineFile}' for '{assembly.Assembly.BaselineAssemblyName}' is missing.")
                    .AppendLine("Generate it with: eng/script/update-public-api-baselines.ps1")
                    .AppendLine();
                continue;
            }

            IReadOnlyList<string> baseline = PublicApiSnapshotBuilder.ReadLines(File.ReadAllText(baselinePath));
            PublicApiDifference difference = PublicApiDifference.Compare(baseline, assembly.Snapshot.Lines);

            if (!difference.IsEmpty)
            {
                failures.AppendLine(difference.Describe(assembly.Assembly.BaselineAssemblyName));
            }
        }

        Assert.True(failures.Length == 0, failures.ToString());
    }

    [Fact]
    public void CheckedInBaselinesUseStableUtf8LineFormat()
    {
        ShippedAssemblyManifest manifest = ShippedAssemblyManifest.Load();

        foreach (ShippedAssemblyManifest.ShippedAssembly assembly in manifest.Assemblies)
        {
            string path = assembly.GetBaselinePath();
            Assert.True(File.Exists(path), $"The baseline '{assembly.BaselineFile}' is missing.");

            byte[] bytes = File.ReadAllBytes(path);

            Assert.False(
                bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                $"'{assembly.BaselineFile}' must be UTF-8 without a byte order mark.");

            string content = Encoding.UTF8.GetString(bytes);

            Assert.DoesNotContain('\r', content);
            Assert.EndsWith("\n", content, StringComparison.Ordinal);

            string[] lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.All(lines, line => Assert.Equal(line.TrimEnd(), line));
            Assert.Contains(lines, line => line.StartsWith('#'));

            IReadOnlyList<string> records = PublicApiSnapshotBuilder.ReadLines(content);
            Assert.NotEmpty(records);
            Assert.Equal(records.Count, records.Distinct(StringComparer.Ordinal).Count());
            Assert.All(records, line => Assert.Equal(3, line.Split(" | ", 3, StringSplitOptions.None).Length));
        }
    }

    [Fact]
    public void CheckedInBaselinesAreSortedDeterministically()
    {
        foreach (CompiledAssembly assembly in CompiledPublicApi.Assemblies)
        {
            string baselinePath = ResolveBaselinePath(assembly.Assembly);
            if (!File.Exists(baselinePath))
            {
                continue;
            }

            IReadOnlyList<string> baseline = PublicApiSnapshotBuilder.ReadLines(File.ReadAllText(baselinePath));
            var lookup = new HashSet<string>(assembly.Snapshot.Lines, StringComparer.Ordinal);

            if (!baseline.All(lookup.Contains))
            {
                continue;
            }

            Assert.Equal(assembly.Snapshot.Lines, baseline);
        }
    }

    /// <summary>
    /// Writes regenerated baselines under <c>out/public-api-candidates</c> when explicitly requested.
    /// </summary>
    /// <remarks>
    /// This never writes a checked-in baseline. It is a no-op unless
    /// <see cref="UpdateEnvironmentVariable"/> is set to <c>1</c> by the update script.
    /// </remarks>
    [Fact]
    public void WriteCandidateBaselines()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(UpdateEnvironmentVariable), "1", StringComparison.Ordinal))
        {
            return;
        }

        string candidateDirectory = RepositoryPaths.Combine(CandidateDirectory);
        if (Directory.Exists(candidateDirectory))
        {
            Directory.Delete(candidateDirectory, recursive: true);
        }

        Directory.CreateDirectory(candidateDirectory);

        foreach (CompiledAssembly assembly in CompiledPublicApi.Assemblies)
        {
            string content = PublicApiSnapshotBuilder.Render(assembly.Snapshot.Records);
            string path = Path.Combine(candidateDirectory, Path.GetFileName(assembly.Assembly.BaselineFile));

            File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            Assert.Equal(
                assembly.Snapshot.Lines,
                PublicApiSnapshotBuilder.ReadLines(File.ReadAllText(path)));
        }
    }

    private static string ResolveBaselinePath(ShippedAssemblyManifest.ShippedAssembly assembly)
    {
        string overrideDirectory = Environment.GetEnvironmentVariable(BaselineDirectoryEnvironmentVariable);

        return string.IsNullOrEmpty(overrideDirectory)
            ? assembly.GetBaselinePath()
            : Path.Combine(overrideDirectory, Path.GetFileName(assembly.BaselineFile));
    }
}
