// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace Microsoft.Azure.WebJobs.Script.PublicApi.Tests;

/// <summary>
/// The authoritative mapping of shipped packages to the first-party compiled assemblies they contain.
/// </summary>
internal sealed class ShippedAssemblyManifest
{
    /// <summary>
    /// The repository-relative path of the manifest file.
    /// </summary>
    public const string RelativePath = "test/WebJobs.Script.PublicApi.Tests/ShippedAssemblyManifest.json";

    /// <summary>
    /// The repository-relative directory that owns the manifest and the checked-in baselines.
    /// </summary>
    public const string ProjectRelativePath = "test/WebJobs.Script.PublicApi.Tests";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>
    /// Gets or sets the manifest format version.
    /// </summary>
    public int FormatVersion { get; set; }

    /// <summary>
    /// Gets or sets the repository-relative path of the official pack job that defines the shipped package set.
    /// </summary>
    public string PackJobTemplate { get; set; }

    /// <summary>
    /// Gets or sets the shipped package entries.
    /// </summary>
    public ShippedPackage[] Packages { get; set; } = Array.Empty<ShippedPackage>();

    /// <summary>
    /// Gets every shipped assembly asset across all packages.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<ShippedAssembly> Assemblies => Packages.SelectMany(package => package.Assemblies).ToArray();

    /// <summary>
    /// Gets or sets the projects that intentionally ship no first-party compiled API of their own.
    /// </summary>
    public NonShippedProject[] NonShippedProjects { get; set; } = Array.Empty<NonShippedProject>();

    /// <summary>
    /// Loads the manifest from the repository.
    /// </summary>
    /// <returns>The manifest.</returns>
    public static ShippedAssemblyManifest Load()
    {
        string path = RepositoryPaths.Combine(RelativePath);
        ShippedAssemblyManifest manifest = JsonSerializer.Deserialize<ShippedAssemblyManifest>(File.ReadAllText(path), SerializerOptions);

        if (manifest is null)
        {
            throw new InvalidOperationException($"Unable to read the shipped assembly manifest '{path}'.");
        }

        foreach (ShippedPackage package in manifest.Packages)
        {
            foreach (ShippedAssembly assembly in package.Assemblies)
            {
                assembly.Package = package;
            }
        }

        return manifest;
    }

    /// <summary>
    /// Gets the directories used to resolve dependencies of every inspected assembly.
    /// </summary>
    /// <returns>The absolute probe directories.</returns>
    public IReadOnlyList<string> GetProbeDirectories()
    {
        return Assemblies
            .Select(assembly => Path.GetDirectoryName(RepositoryPaths.Combine(assembly.ReleaseOutput)))
            .Where(directory => !string.IsNullOrEmpty(directory))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(directory => directory, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// A package produced by the official pack job and the first-party assembly it ships.
    /// </summary>
    internal sealed class ShippedPackage
    {
        /// <summary>
        /// Gets or sets the NuGet package identifier.
        /// </summary>
        public string PackageId { get; set; }

        /// <summary>
        /// Gets or sets the repository-relative path of the packed project.
        /// </summary>
        public string PackageProject { get; set; }

        /// <summary>
        /// Gets or sets the assembly name declared by the packed project.
        /// </summary>
        public string PackageProjectAssemblyName { get; set; }

        /// <summary>
        /// Gets or sets the target frameworks declared by the packed project.
        /// </summary>
        public string[] PackageProjectTargetFrameworks { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Gets or sets a value indicating whether the packed project's own build output is shipped.
        /// </summary>
        public bool PackageProjectIncludesBuildOutput { get; set; }

        /// <summary>
        /// Gets or sets the explicit MSBuild target that collects an external assembly into the package.
        /// </summary>
        public string PackageProjectOutputCollectionTarget { get; set; }

        /// <summary>
        /// Gets or sets the explicit package path used by <see cref="PackageProjectOutputCollectionTarget"/>.
        /// </summary>
        public string PackageProjectOutputCollectionPackagePath { get; set; }

        /// <summary>
        /// Gets or sets the first-party compiled assemblies shipped by this package.
        /// </summary>
        public ShippedAssembly[] Assemblies { get; set; } = Array.Empty<ShippedAssembly>();
    }

    /// <summary>
    /// One target-framework-qualified first-party compiled assembly shipped in a package.
    /// </summary>
    internal sealed class ShippedAssembly
    {
        /// <summary>
        /// Gets the package that owns this asset.
        /// </summary>
        [JsonIgnore]
        public ShippedPackage Package { get; set; }

        /// <summary>
        /// Gets or sets the path of the assembly inside the package.
        /// </summary>
        public string PackageAssetPath { get; set; }

        /// <summary>
        /// Gets or sets the simple name of the baselined first-party assembly.
        /// </summary>
        public string BaselineAssemblyName { get; set; }

        /// <summary>
        /// Gets or sets the repository-relative path of the project that compiles the baselined assembly.
        /// </summary>
        public string BaselineAssemblyProject { get; set; }

        /// <summary>
        /// Gets or sets the target framework of the baselined assembly.
        /// </summary>
        public string BaselineAssemblyTargetFramework { get; set; }

        /// <summary>
        /// Gets or sets the framework name recorded in the baselined assembly's metadata.
        /// </summary>
        public string BaselineAssemblyFrameworkName { get; set; }

        /// <summary>
        /// Gets or sets the repository-relative Release build output of the baselined assembly.
        /// </summary>
        public string ReleaseOutput { get; set; }

        /// <summary>
        /// Gets or sets the project-relative path of the checked-in baseline.
        /// </summary>
        public string BaselineFile { get; set; }

        /// <summary>
        /// Gets the absolute path of the Release build output.
        /// </summary>
        /// <returns>The absolute assembly path.</returns>
        public string GetReleaseOutputPath()
        {
            return RepositoryPaths.Combine(ReleaseOutput);
        }

        /// <summary>
        /// Gets the absolute path of the checked-in baseline file.
        /// </summary>
        /// <returns>The absolute baseline path.</returns>
        public string GetBaselinePath()
        {
            return RepositoryPaths.Combine($"{ProjectRelativePath}/{BaselineFile}");
        }
    }

    /// <summary>
    /// A repository project that intentionally ships no first-party compiled API of its own.
    /// </summary>
    internal sealed class NonShippedProject
    {
        /// <summary>
        /// Gets or sets the repository-relative project path.
        /// </summary>
        public string Project { get; set; }

        /// <summary>
        /// Gets or sets the reason the project is excluded from the compiled API gate.
        /// </summary>
        public string Reason { get; set; }
    }
}

/// <summary>
/// Reads the minimal MSBuild facts required to validate the shipped assembly manifest.
/// </summary>
internal sealed class ProjectFacts
{
    private ProjectFacts(XDocument document, string projectPath)
    {
        string projectName = Path.GetFileNameWithoutExtension(projectPath);
        AssemblyName = GetProperty(document, "AssemblyName") ?? projectName;
        PackageId = GetProperty(document, "PackageId") ?? AssemblyName;
        IncludeBuildOutput = !string.Equals(GetProperty(document, "IncludeBuildOutput"), "false", StringComparison.OrdinalIgnoreCase);

        string single = GetProperty(document, "TargetFramework");
        string multiple = GetProperty(document, "TargetFrameworks");
        TargetFrameworks = single is not null
            ? new[] { single }
            : multiple?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? Array.Empty<string>();

        Targets = document.Root
            ?.Elements()
            .Where(element => string.Equals(element.Name.LocalName, "Target", StringComparison.Ordinal))
            .ToArray() ?? Array.Empty<XElement>();
    }

    /// <summary>
    /// Gets the effective package identifier.
    /// </summary>
    public string PackageId { get; }

    /// <summary>
    /// Gets the effective assembly name.
    /// </summary>
    public string AssemblyName { get; }

    /// <summary>
    /// Gets the declared target frameworks.
    /// </summary>
    public IReadOnlyList<string> TargetFrameworks { get; }

    /// <summary>
    /// Gets a value indicating whether the project's own build output is packed.
    /// </summary>
    public bool IncludeBuildOutput { get; }

    /// <summary>
    /// Gets the declared MSBuild targets.
    /// </summary>
    public IReadOnlyList<XElement> Targets { get; }

    /// <summary>
    /// Loads project facts from a repository-relative project path.
    /// </summary>
    /// <param name="relativePath">The repository-relative project path.</param>
    /// <returns>The project facts.</returns>
    public static ProjectFacts Load(string relativePath)
    {
        string path = RepositoryPaths.Combine(relativePath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"The manifest references project '{relativePath}', which does not exist.", path);
        }

        return new ProjectFacts(XDocument.Load(path), path);
    }

    /// <summary>
    /// Gets the package paths declared by a named target's <c>Content</c> items.
    /// </summary>
    /// <param name="targetName">The target name.</param>
    /// <returns>The declared package paths.</returns>
    public IReadOnlyList<string> GetTargetContentPackagePaths(string targetName)
    {
        return Targets
            .Where(target => string.Equals((string)target.Attribute("Name"), targetName, StringComparison.Ordinal))
            .SelectMany(target => target.Descendants())
            .Where(element => string.Equals(element.Name.LocalName, "Content", StringComparison.Ordinal))
            .Select(element => (string)element.Attribute("PackagePath"))
            .Where(value => !string.IsNullOrEmpty(value))
            .ToArray();
    }

    private static string GetProperty(XDocument document, string name)
    {
        return document.Root
            ?.Elements()
            .Where(element => string.Equals(element.Name.LocalName, "PropertyGroup", StringComparison.Ordinal))
            .SelectMany(group => group.Elements())
            .Where(element => string.Equals(element.Name.LocalName, name, StringComparison.Ordinal))
            .Select(element => element.Value.Trim())
            .LastOrDefault();
    }
}
