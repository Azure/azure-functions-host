// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Config.Tests;

public class EnvironmentBehaviorParityTests
{
    private const string BooleanHelperSignaturePrefix =
        "method System.Boolean Microsoft.Azure.WebJobs.Script.EnvironmentExtensions.";

    private const string EnvironmentOnlyParameterList =
        "(this Microsoft.Azure.WebJobs.Script.IEnvironment environment)";

    private const string BaselineFileName = "EnvironmentBehaviorParityBaseline.json";

    private const string InventoryRelativePath =
        "StaticAnalysis/EnvironmentMigration/EnvironmentMigrationInventory.json";

    [Fact]
    public async Task CompiledHelpersMatchInventoryAndGoldenBaseline()
    {
        EnvironmentHelperMatrixResult actual =
            await EnvironmentContractTestHostRunner.RunScenarioAsync<EnvironmentHelperMatrixResult>(
                EnvironmentBehaviorParityTestContracts.HelperMatrixScenario);
        EnvironmentHelperMatrixResult expected = ReadBaseline();
        string[] inventory = ReadCompiledHelperInventory();

        Assert.Equal(11, actual.Profiles.Length);
        Assert.Equal(11, expected.Profiles.Length);
        Assert.Equal(
            Enum.GetValues<HostingEnvironmentProfile>().OrderBy(value => value),
            actual.Profiles.Select(profile => profile.Profile).OrderBy(value => value));
        Assert.Equal(
            Enum.GetValues<HostingEnvironmentProfile>().OrderBy(value => value),
            expected.Profiles.Select(profile => profile.Profile).OrderBy(value => value));

        foreach (EnvironmentProfileResult actualProfile in actual.Profiles)
        {
            EnvironmentProfileContract profileContract = Assert.Single(
                EnvironmentBehaviorParityFixtures.CompleteProfiles,
                profile => profile.Profile == actualProfile.Profile);
            EnvironmentProfileResult expectedProfile = Assert.Single(
                expected.Profiles,
                profile => profile.Profile == actualProfile.Profile);
            Assert.Equal(profileContract.DefaultPlatform, actualProfile.Platform);
            Assert.True(actualProfile.Is64BitProcess);
            Assert.Equal(expectedProfile.Platform, actualProfile.Platform);
            Assert.Equal(expectedProfile.Is64BitProcess, actualProfile.Is64BitProcess);
            AssertHelperSet(
                actualProfile.Profile.ToString(),
                expectedProfile.Helpers,
                actualProfile.Helpers,
                inventory);
        }

        Assert.Equal(4, actual.StableFactVariants.Length);
        Assert.Equal(4, expected.StableFactVariants.Length);
        foreach (EnvironmentStableFactVariantResult actualVariant in actual.StableFactVariants)
        {
            EnvironmentStableFactVariantResult expectedVariant = Assert.Single(
                expected.StableFactVariants,
                variant => string.Equals(
                    variant.Name,
                    actualVariant.Name,
                    StringComparison.Ordinal));
            Assert.Equal(expectedVariant.Profile, actualVariant.Profile);
            Assert.Equal(expectedVariant.Platform, actualVariant.Platform);
            Assert.Equal(expectedVariant.Is64BitProcess, actualVariant.Is64BitProcess);
            AssertHelperSet(
                actualVariant.Name,
                expectedVariant.Helpers,
                actualVariant.Helpers,
                inventory);
        }

        Assert.Equal(expected.Observations.Length, actual.Observations.Length);
        for (int i = 0; i < expected.Observations.Length; i++)
        {
            EnvironmentMarkerObservationResult expectedObservation =
                expected.Observations[i];
            EnvironmentMarkerObservationResult actualObservation =
                actual.Observations[i];
            Assert.Equal(expectedObservation.Name, actualObservation.Name);
            Assert.Equal(expectedObservation.Profile, actualObservation.Profile);
            Assert.Equal(expectedObservation.Evidence, actualObservation.Evidence);
            Assert.Equal(expectedObservation.Phase, actualObservation.Phase);
            Assert.True(
                new PredicateDictionaryComparer().Equals(
                    expectedObservation.Predicates,
                    actualObservation.Predicates),
                $"Predicate output changed for '{actualObservation.Name}'.");
        }
    }

    [Fact]
    public void StableFactVariantsCoverPlatformBitnessProcessorCountAndVmss()
    {
        EnvironmentHelperMatrixResult baseline = ReadBaseline();
        Assert.Equal(
            EnvironmentBehaviorParityFixtures.StableFactVariants
                .Select(variant => variant.Name)
                .OrderBy(value => value, StringComparer.Ordinal),
            baseline.StableFactVariants
                .Select(variant => variant.Name)
                .OrderBy(value => value, StringComparer.Ordinal));
        foreach (EnvironmentStableFactVariantContract variantContract in
            EnvironmentBehaviorParityFixtures.StableFactVariants)
        {
            EnvironmentStableFactVariantResult variant = GetStableFactVariant(
                baseline,
                variantContract.Name);
            Assert.Equal(variantContract.Profile, variant.Profile);
            Assert.Equal(variantContract.Platform, variant.Platform);
            Assert.Equal(variantContract.Is64BitProcess, variant.Is64BitProcess);
        }

        EnvironmentProfileResult localWindows64 = Assert.Single(
            baseline.Profiles,
            profile => profile.Profile == HostingEnvironmentProfile.LocalSelfHost);
        EnvironmentStableFactVariantResult localLinux64 = GetStableFactVariant(
            baseline,
            "LocalSelfHost:Linux64Bit");
        EnvironmentStableFactVariantResult coreToolsLinux64 = GetStableFactVariant(
            baseline,
            "CoreTools:Linux64Bit");
        EnvironmentStableFactVariantResult localWindows32 = GetStableFactVariant(
            baseline,
            "LocalSelfHost:Windows32Bit");
        EnvironmentStableFactVariantResult windowsConsumptionNonVmss = GetStableFactVariant(
            baseline,
            "WindowsConsumption:WindowsNonVmss");
        EnvironmentProfileResult coreToolsWindows64 = Assert.Single(
            baseline.Profiles,
            profile => profile.Profile == HostingEnvironmentProfile.CoreTools);
        EnvironmentProfileResult windowsConsumptionVmss = Assert.Single(
            baseline.Profiles,
            profile => profile.Profile == HostingEnvironmentProfile.WindowsConsumption);

        Assert.True(localWindows64.Helpers.SequenceEqual(localLinux64.Helpers));
        Assert.True(coreToolsWindows64.Helpers.SequenceEqual(coreToolsLinux64.Helpers));
        Assert.True(localWindows64.Is64BitProcess);
        Assert.True(localWindows64.Helpers.SequenceEqual(localWindows32.Helpers));
        Assert.False(localWindows32.Is64BitProcess);

        Assert.Equal("true", GetHelperValue(windowsConsumptionVmss.Helpers, "IsVMSS"));
        Assert.Equal(
            "$PROCESSOR_COUNT",
            GetHelperValue(windowsConsumptionVmss.Helpers, "GetEffectiveCoresCount"));
        Assert.Equal("false", GetHelperValue(windowsConsumptionNonVmss.Helpers, "IsVMSS"));
        Assert.Equal(
            "1",
            GetHelperValue(windowsConsumptionNonVmss.Helpers, "GetEffectiveCoresCount"));
    }

    private static void AssertHelperSet(
        string scope,
        EnvironmentHelperResult[] expected,
        EnvironmentHelperResult[] actual,
        string[] inventory)
    {
        Assert.Equal(77, actual.Length);
        Assert.Equal(77, expected.Length);
        Assert.Equal(
            inventory,
            actual.Select(helper => helper.Signature).OrderBy(value => value, StringComparer.Ordinal));
        Assert.Equal(
            inventory,
            expected.Select(helper => helper.Signature).OrderBy(value => value, StringComparer.Ordinal));
        for (int i = 0; i < expected.Length; i++)
        {
            EnvironmentHelperResult expectedHelper = expected[i];
            EnvironmentHelperResult actualHelper = actual[i];
            Assert.Equal(expectedHelper.Signature, actualHelper.Signature);
            Assert.True(
                string.Equals(
                    expectedHelper.Value,
                    actualHelper.Value,
                    StringComparison.Ordinal),
                $"{scope}/{actualHelper.Signature}: expected '{expectedHelper.Value}', actual '{actualHelper.Value}'.");
        }
    }

    private static EnvironmentStableFactVariantResult GetStableFactVariant(
        EnvironmentHelperMatrixResult baseline,
        string name)
    {
        return Assert.Single(
            baseline.StableFactVariants,
            variant => string.Equals(variant.Name, name, StringComparison.Ordinal));
    }

    private static string GetHelperValue(
        EnvironmentHelperResult[] helpers,
        string methodName)
    {
        EnvironmentHelperResult helper = Assert.Single(
            helpers,
            candidate => candidate.Signature.Contains(
                $".{methodName}(",
                StringComparison.Ordinal));

        return helper.Value;
    }

    [Fact]
    public void MarkerMatrixCoversEveryProfilePhaseAndEvidenceKind()
    {
        EnvironmentHelperMatrixResult baseline = ReadBaseline();
        Assert.Equal(87, baseline.Observations.Length);
        Assert.Equal(
            baseline.Observations.Length,
            baseline.Observations.Select(row => row.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            5,
            baseline.Observations.Count(row => row.Evidence == MarkerEvidence.Incomplete));
        Assert.Equal(
            5,
            baseline.Observations.Count(row => row.Evidence == MarkerEvidence.ContradictoryOrOverlapping));

        string[] inventory = ReadCompiledHelperInventory();
        string[] expectedPredicateNames = ReadEnvironmentMarkerPredicateInventory(inventory);
        Assert.Equal(52, expectedPredicateNames.Length);
        foreach (EnvironmentMarkerObservationResult observation in baseline.Observations)
        {
            Assert.Equal(
                expectedPredicateNames,
                observation.Predicates.Keys.OrderBy(value => value, StringComparer.Ordinal));
        }

        EnvironmentMarkerObservationResult[] complete = baseline.Observations
            .Where(row => row.Evidence == MarkerEvidence.CompleteCurrentInputs)
            .ToArray();

        Assert.Equal(
            Enum.GetValues<HostingEnvironmentProfile>().Length
                * Enum.GetValues<HostPhase>().Length,
            complete.Length);

        foreach (HostingEnvironmentProfile profile in Enum.GetValues<HostingEnvironmentProfile>())
        {
            EnvironmentMarkerObservationResult[] profileRows = complete
                .Where(row => row.Profile == profile)
                .ToArray();
            Assert.Equal(
                Enum.GetValues<HostPhase>().OrderBy(value => value),
                profileRows.Select(row => row.Phase).OrderBy(value => value));

            foreach (EnvironmentMarkerObservationResult row in profileRows)
            {
                Assert.Equal(
                    EnvironmentBehaviorParityFixtures.IsPlaceholderPhase(row.Phase)
                        .ToString()
                        .ToLowerInvariant(),
                    row.Predicates["IsPlaceholderModeEnabled"]);
            }

            Dictionary<string, string>[] classificationVectors = profileRows
                .Select(row => row.Predicates
                    .Where(pair => !string.Equals(
                        pair.Key,
                        "IsPlaceholderModeEnabled",
                        StringComparison.Ordinal))
                    .ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value,
                        StringComparer.Ordinal))
                .Distinct(new PredicateDictionaryComparer())
                .ToArray();
            Assert.Single(classificationVectors);
        }

        Assert.Contains(
            baseline.Observations,
            row => row.Evidence == MarkerEvidence.Incomplete);
        Assert.Contains(
            baseline.Observations,
            row => row.Evidence == MarkerEvidence.ContradictoryOrOverlapping);
    }

    private static string[] ReadEnvironmentMarkerPredicateInventory(string[] inventory)
    {
        string[] booleanHelpers = inventory
            .Where(signature => signature.StartsWith(
                BooleanHelperSignaturePrefix,
                StringComparison.Ordinal))
            .ToArray();
        string[] markerPredicates = booleanHelpers
            .Where(signature => signature.EndsWith(
                EnvironmentOnlyParameterList,
                StringComparison.Ordinal))
            .ToArray();
        string[] excluded = booleanHelpers
            .Except(markerPredicates, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "method System.Boolean Microsoft.Azure.WebJobs.Script.EnvironmentExtensions.IsInProc(this Microsoft.Azure.WebJobs.Script.IEnvironment environment, System.String workerRuntime = null)",
                "method System.Boolean Microsoft.Azure.WebJobs.Script.EnvironmentExtensions.TryGetFunctionsTargetGroup(this Microsoft.Azure.WebJobs.Script.IEnvironment environment, out System.String group)"
            ],
            excluded);

        return markerPredicates
            .Select(signature =>
            {
                int nameStart = BooleanHelperSignaturePrefix.Length;
                int nameEnd = signature.IndexOf('(', nameStart);
                return signature[nameStart..nameEnd];
            })
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }

    [Fact]
    public void IncompleteAndContradictoryMarkersPreserveLegacyOutputs()
    {
        EnvironmentMarkerObservationResult[] observations = ReadBaseline().Observations;

        AssertPredicate(observations, "incomplete:container-only", "IsLinuxConsumptionOnAtlas", true);
        AssertPredicate(observations, "incomplete:container-only", "IsAnyLinuxConsumption", true);

        AssertPredicate(observations, "contradictory:app-service-and-atlas", "IsAppService", true);
        AssertPredicate(observations, "contradictory:app-service-and-atlas", "IsLinuxConsumptionOnAtlas", false);

        AssertPredicate(observations, "contradictory:atlas-and-legion", "IsLinuxConsumptionOnAtlas", false);
        AssertPredicate(observations, "contradictory:atlas-and-legion", "IsFlexConsumptionSku", true);

        AssertPredicate(observations, "contradictory:flex-and-dynamic-legion", "IsFlexConsumptionSku", true);
        AssertPredicate(observations, "contradictory:flex-and-dynamic-legion", "IsLinuxConsumptionOnLegion", true);

        AssertPredicate(observations, "contradictory:container-apps-and-kubernetes", "IsManagedAppEnvironment", true);
        AssertPredicate(observations, "contradictory:container-apps-and-kubernetes", "IsKubernetesManagedHosting", false);
        AssertPredicate(observations, "contradictory:container-apps-and-kubernetes", "IsAnyKubernetesEnvironment", true);

        AssertPredicate(observations, "contradictory:core-tools-and-hosted", "IsCoreTools", true);
        AssertPredicate(observations, "contradictory:core-tools-and-hosted", "IsWindowsConsumption", true);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task StaticCachesCaptureFirstEligibleValue(
        bool initialMultiLanguage,
        bool initialApplicationInsights)
    {
        StaticCacheContractResult result =
            await EnvironmentContractTestHostRunner.RunScenarioAsync<StaticCacheContractResult>(
                EnvironmentBehaviorParityTestContracts.StaticCacheScenario,
                $"{initialMultiLanguage},{initialApplicationInsights}");

        Assert.False(result.PlaceholderApplicationInsights);
        Assert.Equal(
            initialApplicationInsights,
            result.SpecializedApplicationInsights);
        Assert.Equal(
            initialApplicationInsights,
            result.MutatedApplicationInsights);
        Assert.Equal(initialMultiLanguage, result.InitialMultiLanguage);
        Assert.Equal(initialMultiLanguage, result.MutatedMultiLanguage);
    }

    private static void AssertPredicate(
        EnvironmentMarkerObservationResult[] observations,
        string observationName,
        string predicate,
        bool expected)
    {
        EnvironmentMarkerObservationResult observation = Assert.Single(
            observations,
            row => string.Equals(row.Name, observationName, StringComparison.Ordinal));
        Assert.Equal(expected.ToString().ToLowerInvariant(), observation.Predicates[predicate]);
    }

    private static EnvironmentHelperMatrixResult ReadBaseline()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "TestFixture",
            BaselineFileName);
        return JsonSerializer.Deserialize<EnvironmentHelperMatrixResult>(
            File.ReadAllText(path),
            EnvironmentBehaviorParityTestContracts.SerializerOptions)
            ?? throw new InvalidOperationException($"Unable to deserialize '{path}'.");
    }

    private static string[] ReadCompiledHelperInventory(
        [CallerFilePath] string sourceFilePath = "")
    {
        string path = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(sourceFilePath),
            "..",
            InventoryRelativePath));
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement
            .GetProperty("environmentExtensionHelpers")
            .EnumerateArray()
            .Select(value => value.GetString())
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private sealed class PredicateDictionaryComparer :
        IEqualityComparer<Dictionary<string, string>>
    {
        public bool Equals(
            Dictionary<string, string> left,
            Dictionary<string, string> right)
        {
            return left is not null
                && right is not null
                && left.Count == right.Count
                && left.All(pair =>
                    right.TryGetValue(pair.Key, out string value)
                    && string.Equals(pair.Value, value, StringComparison.Ordinal));
        }

        public int GetHashCode(Dictionary<string, string> value)
        {
            HashCode hash = default;
            foreach ((string key, string item) in value.OrderBy(
                pair => pair.Key,
                StringComparer.Ordinal))
            {
                hash.Add(key, StringComparer.Ordinal);
                hash.Add(item, StringComparer.Ordinal);
            }

            return hash.ToHashCode();
        }
    }
}
