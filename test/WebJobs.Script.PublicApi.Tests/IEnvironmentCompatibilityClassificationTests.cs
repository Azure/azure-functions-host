// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.PublicApi.Tests;

/// <summary>
/// Requires a complete, non-wildcard classification of the current <c>IEnvironment</c> migration surface.
/// </summary>
/// <remarks>
/// New or stale entries fail in both directions and no default category is applied, so a migration
/// slice cannot add or delete migration surface without recording its compatibility treatment.
/// </remarks>
public class IEnvironmentCompatibilityClassificationTests
{
    private static readonly string[] ExpectedCategories =
    {
        "core-tools-required",
        "effectively-internal",
        "exported-legacy-static-bridge",
        "host-internal-exported-surface",
        "test-only-legacy-seam"
    };

    [Fact]
    public void ClassificationCoversTheEntireMigrationSurfaceExactly()
    {
        IEnvironmentCompatibilityClassification classification = IEnvironmentCompatibilityClassification.Load();
        IEnvironmentCompatibilityClassification.MigrationInventory inventory = IEnvironmentCompatibilityClassification.LoadInventory();

        string[] expected = inventory.PublicSignatures
            .Concat(inventory.EnvironmentExtensionHelpers)
            .Concat(inventory.GetTestEnvironmentSignatures())
            .ToArray();

        (IReadOnlyList<string> newEntries, IReadOnlyList<string> staleEntries) = SetComparison.Compare(
            expected,
            classification.Entries.Select(entry => entry.Signature));

        Assert.True(
            newEntries.Count == 0 && staleEntries.Count == 0,
            SetComparison.Describe(
                "The IEnvironment compatibility classification",
                "Every migration signature must be classified exactly once, and a classified signature that no longer exists must be removed in the same change.",
                newEntries,
                staleEntries));

        Assert.Equal(expected.Length, classification.Entries.Length);
        Assert.Equal(
            classification.Entries.Length,
            classification.Entries.Select(entry => entry.Signature).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void ClassificationInputsMatchTheLiveCompiledMigrationSurface()
    {
        IEnvironmentCompatibilityClassification.MigrationInventory inventory = IEnvironmentCompatibilityClassification.LoadInventory();
        MigrationSurface surface = MigrationSurfaceScanner.Current;

        (IReadOnlyList<string> newSignatures, IReadOnlyList<string> staleSignatures) = SetComparison.Compare(
            inventory.PublicSignatures,
            surface.PublicSignatures);

        Assert.True(
            newSignatures.Count == 0 && staleSignatures.Count == 0,
            SetComparison.Describe(
                "The compiled IEnvironment migration signatures",
                "The classified migration surface is recomputed from the Release assemblies. Refresh the Phase 0 inventory and the classification together.",
                newSignatures,
                staleSignatures));

        (IReadOnlyList<string> newHelpers, IReadOnlyList<string> staleHelpers) = SetComparison.Compare(
            inventory.EnvironmentExtensionHelpers,
            surface.EnvironmentExtensionHelpers);

        Assert.True(
            newHelpers.Count == 0 && staleHelpers.Count == 0,
            SetComparison.Describe(
                "The compiled EnvironmentExtensions helpers",
                "The classified helper set is recomputed from the Release assemblies. Refresh the Phase 0 inventory and the classification together.",
                newHelpers,
                staleHelpers));
    }

    [Fact]
    public void EveryEntryDeclaresAnExplicitCategoryAndRationale()
    {
        IEnvironmentCompatibilityClassification classification = IEnvironmentCompatibilityClassification.Load();

        Assert.Equal(1, classification.FormatVersion);
        Assert.Equal(
            ExpectedCategories,
            classification.Categories.Select(category => category.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
        Assert.All(classification.Categories, category => Assert.False(string.IsNullOrWhiteSpace(category.Description)));

        var declared = new HashSet<string>(classification.Categories.Select(category => category.Name), StringComparer.Ordinal);

        Assert.All(classification.Entries, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Signature));
            Assert.DoesNotContain('*', entry.Signature);
            Assert.Contains(entry.Category, declared);
            Assert.False(string.IsNullOrWhiteSpace(entry.EffectiveVisibility));
            Assert.False(string.IsNullOrWhiteSpace(entry.Evidence));
            Assert.False(string.IsNullOrWhiteSpace(entry.CompatibilityTreatment));
            Assert.False(string.IsNullOrWhiteSpace(entry.EarliestRemovalGate));
        });
    }

    [Fact]
    public void SurfaceValuesMatchTheirInventorySource()
    {
        IEnvironmentCompatibilityClassification classification = IEnvironmentCompatibilityClassification.Load();
        IEnvironmentCompatibilityClassification.MigrationInventory inventory = IEnvironmentCompatibilityClassification.LoadInventory();

        AssertSurface(classification, inventory.PublicSignatures, IEnvironmentCompatibilityClassification.CompiledExportedApiSurface);
        AssertSurface(classification, inventory.EnvironmentExtensionHelpers, IEnvironmentCompatibilityClassification.EnvironmentExtensionsHelperSurface);
        AssertSurface(classification, inventory.GetTestEnvironmentSignatures(), IEnvironmentCompatibilityClassification.TestEnvironmentSeamSurface);
    }

    [Fact]
    public void CoreToolsRequiredEntriesMatchTheAuditedContract()
    {
        IEnvironmentCompatibilityClassification classification = IEnvironmentCompatibilityClassification.Load();
        CoreToolsCompatibilityContract contract = CoreToolsCompatibilityContract.Load();

        IEnvironmentCompatibilityClassification.ClassificationEntry[] required = classification.Entries
            .Where(entry => string.Equals(entry.Category, "core-tools-required", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(contract.Preserve.Length, required.Length);
        Assert.Equal(
            contract.Preserve.Select(record => record.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            required.Select(entry => entry.CoreToolsRecordId).OrderBy(id => id, StringComparer.Ordinal).ToArray());

        Assert.All(required, entry =>
        {
            Assert.Equal(IEnvironmentCompatibilityClassification.CompiledExportedApiSurface, entry.Surface);
            Assert.Equal("public", entry.EffectiveVisibility);
        });

        Assert.All(
            classification.Entries.Where(entry => !string.Equals(entry.Category, "core-tools-required", StringComparison.Ordinal)),
            entry => Assert.Null(entry.CoreToolsRecordId));
    }

    [Fact]
    public void EnvironmentExtensionsHelpersAreClassifiedAsEffectivelyInternal()
    {
        IEnvironmentCompatibilityClassification classification = IEnvironmentCompatibilityClassification.Load();

        Assert.False(
            MigrationSurfaceScanner.Current.EnvironmentExtensionsIsPublic,
            "EnvironmentExtensions became externally visible. Its 77 helpers are classified as effectively internal and would now need a compiled baseline entry.");

        IEnvironmentCompatibilityClassification.ClassificationEntry[] helpers = classification.Entries
            .Where(entry => string.Equals(entry.Surface, IEnvironmentCompatibilityClassification.EnvironmentExtensionsHelperSurface, StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(helpers);
        Assert.All(helpers, entry => Assert.Equal("effectively-internal", entry.Category));

        PublicApiSnapshot snapshot = CompiledPublicApi.GetSnapshot("Microsoft.Azure.WebJobs.Script");
        Assert.DoesNotContain(
            snapshot.Records,
            record => record.Identity.StartsWith("Microsoft.Azure.WebJobs.Script.EnvironmentExtensions", StringComparison.Ordinal));
    }

    [Fact]
    public void TestEnvironmentSeamIsNotPartOfAnyShippedAssembly()
    {
        IEnvironmentCompatibilityClassification classification = IEnvironmentCompatibilityClassification.Load();

        IEnvironmentCompatibilityClassification.ClassificationEntry[] seams = classification.Entries
            .Where(entry => string.Equals(entry.Surface, IEnvironmentCompatibilityClassification.TestEnvironmentSeamSurface, StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(seams);
        Assert.All(seams, entry => Assert.Equal("test-only-legacy-seam", entry.Category));

        Assert.All(CompiledPublicApi.Assemblies, assembly => Assert.DoesNotContain(
            assembly.Snapshot.Records,
            record => record.Identity.Contains("Microsoft.Azure.WebJobs.Script.Tests.TestEnvironment", StringComparison.Ordinal)));
    }

    private static void AssertSurface(
        IEnvironmentCompatibilityClassification classification,
        IReadOnlyList<string> signatures,
        string expectedSurface)
    {
        var lookup = classification.Entries.ToDictionary(entry => entry.Signature, StringComparer.Ordinal);

        foreach (string signature in signatures)
        {
            Assert.True(lookup.TryGetValue(signature, out IEnvironmentCompatibilityClassification.ClassificationEntry entry), $"'{signature}' is not classified.");
            Assert.Equal(expectedSurface, entry.Surface);
        }
    }
}
