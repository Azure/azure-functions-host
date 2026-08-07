// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Azure.WebJobs.Script.PublicApi.Tests;

/// <summary>
/// The complete, non-wildcard classification of the current <c>IEnvironment</c> migration surface.
/// </summary>
internal sealed class IEnvironmentCompatibilityClassification
{
    /// <summary>
    /// The repository-relative path of the classification file.
    /// </summary>
    public const string RelativePath = "test/WebJobs.Script.PublicApi.Tests/IEnvironmentCompatibilityClassification.json";

    /// <summary>
    /// The repository-relative path of the Phase 0 migration inventory the classification is derived from.
    /// </summary>
    public const string InventoryRelativePath = "test/WebJobs.Script.Tests/StaticAnalysis/EnvironmentMigration/EnvironmentMigrationInventory.json";

    /// <summary>
    /// The surface value used for compiled, exported migration signatures.
    /// </summary>
    public const string CompiledExportedApiSurface = "compiled-exported-api";

    /// <summary>
    /// The surface value used for <c>EnvironmentExtensions</c> helpers.
    /// </summary>
    public const string EnvironmentExtensionsHelperSurface = "environment-extensions-helper";

    /// <summary>
    /// The surface value used for the <c>TestEnvironment</c> seam.
    /// </summary>
    public const string TestEnvironmentSeamSurface = "test-environment-seam";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>
    /// Gets or sets the classification format version.
    /// </summary>
    public int FormatVersion { get; set; }

    /// <summary>
    /// Gets or sets the human-readable description.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Gets or sets the declared categories.
    /// </summary>
    public ClassificationCategory[] Categories { get; set; } = Array.Empty<ClassificationCategory>();

    /// <summary>
    /// Gets or sets the classified entries.
    /// </summary>
    public ClassificationEntry[] Entries { get; set; } = Array.Empty<ClassificationEntry>();

    /// <summary>
    /// Loads the classification from the repository.
    /// </summary>
    /// <returns>The classification.</returns>
    public static IEnvironmentCompatibilityClassification Load()
    {
        string path = RepositoryPaths.Combine(RelativePath);
        IEnvironmentCompatibilityClassification classification =
            JsonSerializer.Deserialize<IEnvironmentCompatibilityClassification>(File.ReadAllText(path), SerializerOptions);

        return classification ?? throw new InvalidOperationException($"Unable to read the compatibility classification '{path}'.");
    }

    /// <summary>
    /// Loads the Phase 0 migration inventory the classification is derived from.
    /// </summary>
    /// <returns>The inventory.</returns>
    public static MigrationInventory LoadInventory()
    {
        string path = RepositoryPaths.Combine(InventoryRelativePath);
        MigrationInventory inventory = JsonSerializer.Deserialize<MigrationInventory>(File.ReadAllText(path), SerializerOptions);

        return inventory ?? throw new InvalidOperationException($"Unable to read the Phase 0 migration inventory '{path}'.");
    }

    /// <summary>
    /// A declared classification category.
    /// </summary>
    internal sealed class ClassificationCategory
    {
        /// <summary>
        /// Gets or sets the category name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the category description.
        /// </summary>
        public string Description { get; set; }
    }

    /// <summary>
    /// A single classified migration signature.
    /// </summary>
    internal sealed class ClassificationEntry
    {
        /// <summary>
        /// Gets or sets the exact migration signature.
        /// </summary>
        public string Signature { get; set; }

        /// <summary>
        /// Gets or sets the surface the signature belongs to.
        /// </summary>
        public string Surface { get; set; }

        /// <summary>
        /// Gets or sets the effective visibility of the signature.
        /// </summary>
        public string EffectiveVisibility { get; set; }

        /// <summary>
        /// Gets or sets the assigned category.
        /// </summary>
        public string Category { get; set; }

        /// <summary>
        /// Gets or sets the evidence supporting the category.
        /// </summary>
        public string Evidence { get; set; }

        /// <summary>
        /// Gets or sets the compatibility treatment.
        /// </summary>
        public string CompatibilityTreatment { get; set; }

        /// <summary>
        /// Gets or sets the earliest gate at which the signature may be removed.
        /// </summary>
        public string EarliestRemovalGate { get; set; }

        /// <summary>
        /// Gets or sets the Core Tools preserve record identifier, when the entry is a hard contract.
        /// </summary>
        public string CoreToolsRecordId { get; set; }
    }

    /// <summary>
    /// The subset of the Phase 0 migration inventory used by the classification gate.
    /// </summary>
    internal sealed class MigrationInventory
    {
        /// <summary>
        /// Gets or sets the inventory format version.
        /// </summary>
        public int FormatVersion { get; set; }

        /// <summary>
        /// Gets or sets the public <c>EnvironmentExtensions</c> helper signatures.
        /// </summary>
        public string[] EnvironmentExtensionHelpers { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Gets or sets the compiled exported migration signatures.
        /// </summary>
        public string[] PublicSignatures { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Gets or sets the recorded test seams.
        /// </summary>
        public string[] TestSeams { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Gets the compiled <c>TestEnvironment</c> signatures recorded by the inventory.
        /// </summary>
        /// <returns>The signatures.</returns>
        public IReadOnlyList<string> GetTestEnvironmentSignatures()
        {
            return TestSeams
                .Where(seam => seam.StartsWith("TestEnvironmentSignature|", StringComparison.Ordinal))
                .Select(seam => seam.Split('|'))
                .Where(parts => parts.Length >= 3)
                .Select(parts => parts[2])
                .ToArray();
        }
    }
}
