// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.PublicApi.Tests;

/// <summary>
/// Proves that the shipped assembly manifest still describes every package produced by the official
/// pack job and every first-party assembly those packages contain.
/// </summary>
public class ShippedAssemblyManifestTests
{
    private static readonly string[] ShippedProjectSearchRoots = { "src", "tools/ExtensionsMetadataGenerator/src" };

    [Fact]
    public void PackJobProjectListMatchesManifestPackageProjects()
    {
        ShippedAssemblyManifest manifest = ShippedAssemblyManifest.Load();

        IReadOnlyList<string> packed = PackJobReader.ReadPackedProjects(manifest.PackJobTemplate);
        string[] expected = manifest.Packages
            .Select(package => "**/" + Path.GetFileName(package.PackageProject))
            .ToArray();

        (IReadOnlyList<string> newEntries, IReadOnlyList<string> staleEntries) = SetComparison.Compare(expected, packed);

        Assert.True(
            newEntries.Count == 0 && staleEntries.Count == 0,
            SetComparison.Describe(
                $"The packed project list in '{manifest.PackJobTemplate}'",
                "Every packed project must have a shipped assembly manifest entry, and every manifest entry must still be packed.",
                newEntries,
                staleEntries));

        Assert.Equal(manifest.Packages.Length, packed.Count);
    }

    [Fact]
    public void PackageProjectsDeclareManifestMetadata()
    {
        ShippedAssemblyManifest manifest = ShippedAssemblyManifest.Load();

        foreach (ShippedAssemblyManifest.ShippedPackage package in manifest.Packages)
        {
            ProjectFacts facts = ProjectFacts.Load(package.PackageProject);

            Assert.Equal(package.PackageId, facts.PackageId);
            Assert.Equal(package.PackageProjectAssemblyName, facts.AssemblyName);
            Assert.Equal(package.PackageProjectTargetFrameworks, facts.TargetFrameworks);
            Assert.Equal(package.PackageProjectIncludesBuildOutput, facts.IncludeBuildOutput);

            if (package.PackageProjectIncludesBuildOutput)
            {
                Assert.Null(package.PackageProjectOutputCollectionTarget);
                ShippedAssemblyManifest.ShippedAssembly assembly = Assert.Single(package.Assemblies);
                Assert.Equal(package.PackageProjectAssemblyName, assembly.BaselineAssemblyName);
                Assert.Equal($"lib/{assembly.BaselineAssemblyTargetFramework}/{assembly.BaselineAssemblyName}.dll", assembly.PackageAssetPath);
                continue;
            }

            Assert.False(
                string.IsNullOrEmpty(package.PackageProjectOutputCollectionTarget),
                $"'{package.PackageProject}' excludes its own build output, so the manifest must name the target that packs the shipped assembly.");

            Assert.Contains(
                package.PackageProjectOutputCollectionPackagePath,
                facts.GetTargetContentPackagePaths(package.PackageProjectOutputCollectionTarget));
        }
    }

    [Fact]
    public void BaselineAssemblyProjectsDeclareManifestMetadata()
    {
        ShippedAssemblyManifest manifest = ShippedAssemblyManifest.Load();

        foreach (ShippedAssemblyManifest.ShippedAssembly assembly in manifest.Assemblies)
        {
            ProjectFacts facts = ProjectFacts.Load(assembly.BaselineAssemblyProject);

            Assert.Equal(assembly.BaselineAssemblyName, facts.AssemblyName);
            Assert.Contains(assembly.BaselineAssemblyTargetFramework, facts.TargetFrameworks);
            Assert.EndsWith("/" + assembly.BaselineAssemblyName + ".dll", assembly.PackageAssetPath, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CollectedPackageOutputsMatchManifestAssemblies()
    {
        ShippedAssemblyManifest manifest = ShippedAssemblyManifest.Load();
        var firstPartyAssemblyNames = new HashSet<string>(
            manifest.Packages
                .Select(package => package.PackageProject)
                .Concat(manifest.Assemblies.Select(assembly => assembly.BaselineAssemblyProject))
                .Concat(manifest.NonShippedProjects.Select(project => project.Project))
                .Distinct(StringComparer.Ordinal)
                .Select(project => ProjectFacts.Load(project).AssemblyName),
            StringComparer.Ordinal);

        foreach (ShippedAssemblyManifest.ShippedPackage package in manifest.Packages
            .Where(package => !package.PackageProjectIncludesBuildOutput))
        {
            var actualAssets = new List<string>();
            string outputProjectName = Path.GetFileNameWithoutExtension(package.PackageProject);

            foreach (string framework in package.PackageProjectTargetFrameworks)
            {
                string outputDirectory = RepositoryPaths.Combine($"out/bin/{outputProjectName}/release_{framework}");
                Assert.True(
                    Directory.Exists(outputDirectory),
                    $"The Release output '{outputDirectory}' required to verify collected package assets does not exist.");

                actualAssets.AddRange(Directory
                    .EnumerateFiles(outputDirectory, "*.dll", SearchOption.AllDirectories)
                    .Where(path => firstPartyAssemblyNames.Contains(Path.GetFileNameWithoutExtension(path)))
                    .Select(path =>
                        $"tools/{framework}/{Path.GetRelativePath(outputDirectory, path).Replace('\\', '/')}"));
            }

            (IReadOnlyList<string> newEntries, IReadOnlyList<string> staleEntries) = SetComparison.Compare(
                package.Assemblies.Select(assembly => assembly.PackageAssetPath),
                actualAssets);

            Assert.True(
                newEntries.Count == 0 && staleEntries.Count == 0,
                SetComparison.Describe(
                    $"The first-party assemblies collected into package '{package.PackageId}'",
                    "Every first-party DLL copied by the package collection target must have a target-framework-qualified baseline.",
                    newEntries,
                    staleEntries));
        }
    }

    [Fact]
    public void EveryManifestAssemblyHasExactlyOneBaselineAndNoBaselineIsStale()
    {
        ShippedAssemblyManifest manifest = ShippedAssemblyManifest.Load();

        string[] expected = manifest.Assemblies.Select(assembly => assembly.BaselineFile).ToArray();
        Assert.Equal(expected.Length, expected.Distinct(StringComparer.Ordinal).Count());

        string baselineDirectory = RepositoryPaths.Combine($"{ShippedAssemblyManifest.ProjectRelativePath}/Baselines");
        string[] actual = Directory.EnumerateFiles(baselineDirectory, "*.txt", SearchOption.TopDirectoryOnly)
            .Select(path => "Baselines/" + Path.GetFileName(path))
            .ToArray();

        (IReadOnlyList<string> newEntries, IReadOnlyList<string> staleEntries) = SetComparison.Compare(expected, actual);

        Assert.True(
            newEntries.Count == 0 && staleEntries.Count == 0,
            SetComparison.Describe(
                "The checked-in public API baseline set",
                "There must be exactly one baseline per manifest assembly, and no baseline for an assembly that is no longer shipped.",
                newEntries,
                staleEntries));
    }

    [Fact]
    public void ReleaseOutputsMatchManifestAssemblyIdentity()
    {
        ShippedAssemblyManifest manifest = ShippedAssemblyManifest.Load();
        IReadOnlyList<string> probeDirectories = manifest.GetProbeDirectories();

        foreach (ShippedAssemblyManifest.ShippedAssembly assembly in manifest.Assemblies)
        {
            string path = assembly.GetReleaseOutputPath();
            string missingMessage = $"The Release build output '{assembly.ReleaseOutput}' for package '{assembly.Package.PackageId}' does not exist. "
                + "Build the shipped projects in Release before running the public API gate.";

            Assert.True(File.Exists(path), missingMessage);
            Assert.Equal(assembly.BaselineAssemblyName + ".dll", Path.GetFileName(path));

            PublicApiSnapshot snapshot = PublicApiSnapshotBuilder.Create(path, probeDirectories);

            Assert.Equal(assembly.BaselineAssemblyName, snapshot.AssemblyName);
            Assert.Equal(assembly.BaselineAssemblyFrameworkName, GetAssemblyRecord(snapshot, "targetFramework"));
            Assert.Equal(assembly.BaselineAssemblyName, GetAssemblyRecord(snapshot, "name"));
        }
    }

    [Fact]
    public void EveryFirstPartyProjectIsEitherShippedOrExplicitlyExcluded()
    {
        ShippedAssemblyManifest manifest = ShippedAssemblyManifest.Load();

        string[] classified = manifest.Packages
            .Select(package => package.PackageProject)
            .Concat(manifest.Assemblies.Select(assembly => assembly.BaselineAssemblyProject))
            .Concat(manifest.NonShippedProjects.Select(project => project.Project))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        string repositoryRoot = RepositoryPaths.FindRepositoryRoot();
        string[] discovered = ShippedProjectSearchRoots
            .Select(root => Path.Combine(repositoryRoot, root.Replace('/', Path.DirectorySeparatorChar)))
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
            .Select(path => Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        (IReadOnlyList<string> newEntries, IReadOnlyList<string> staleEntries) = SetComparison.Compare(classified, discovered);

        Assert.True(
            newEntries.Count == 0 && staleEntries.Count == 0,
            SetComparison.Describe(
                "The first-party project classification",
                "Every project under a shipped source root must either ship a baselined assembly or be listed in 'nonShippedProjects' with a reason.",
                newEntries,
                staleEntries));

        Assert.All(manifest.NonShippedProjects, project => Assert.False(string.IsNullOrWhiteSpace(project.Reason)));
    }

    [Fact]
    public void ManifestPackagesAreDistinctAndFullyPopulated()
    {
        ShippedAssemblyManifest manifest = ShippedAssemblyManifest.Load();

        Assert.Equal(1, manifest.FormatVersion);
        Assert.NotEmpty(manifest.Packages);
        Assert.Equal(manifest.Packages.Length, manifest.Packages.Select(package => package.PackageId).Distinct(StringComparer.Ordinal).Count());
        Assert.NotEmpty(manifest.Assemblies);
        Assert.Equal(manifest.Assemblies.Count, manifest.Assemblies.Select(assembly => assembly.PackageAssetPath).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(manifest.Assemblies.Count, manifest.Assemblies.Select(assembly => assembly.ReleaseOutput).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(manifest.Assemblies.Count, manifest.Assemblies.Select(assembly => assembly.BaselineFile).Distinct(StringComparer.Ordinal).Count());

        foreach (ShippedAssemblyManifest.ShippedPackage package in manifest.Packages)
        {
            Assert.False(string.IsNullOrWhiteSpace(package.PackageId));
            Assert.False(string.IsNullOrWhiteSpace(package.PackageProject));
            Assert.NotEmpty(package.Assemblies);
        }

        foreach (ShippedAssemblyManifest.ShippedAssembly assembly in manifest.Assemblies)
        {
            Assert.False(string.IsNullOrWhiteSpace(assembly.PackageAssetPath));
            Assert.False(string.IsNullOrWhiteSpace(assembly.BaselineAssemblyName));
            Assert.False(string.IsNullOrWhiteSpace(assembly.BaselineAssemblyFrameworkName));
            Assert.False(string.IsNullOrWhiteSpace(assembly.ReleaseOutput));
            Assert.StartsWith("out/bin/", assembly.ReleaseOutput, StringComparison.Ordinal);
            Assert.StartsWith("Baselines/", assembly.BaselineFile, StringComparison.Ordinal);
            Assert.Contains(assembly, assembly.Package.Assemblies);
        }
    }

    private static string GetAssemblyRecord(PublicApiSnapshot snapshot, string identity)
    {
        return snapshot.Records
            .Single(record => string.Equals(record.Kind, "assembly", StringComparison.Ordinal)
                && string.Equals(record.Identity, identity, StringComparison.Ordinal))
            .Signature;
    }
}
