// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Config.Tests;

/// <summary>
/// Tests for <see cref="ScriptEnvironmentVariablesConfigurationSource"/>.
/// </summary>
public class ScriptEnvironmentVariablesConfigurationSourceTests
{
    private const string TestHostProjectName = "WebJobs.Script.Tests.EnvironmentVariables.TestHost.csproj";

#if DEBUG
    private const string BuildConfiguration = "Debug";
#else
    private const string BuildConfiguration = "Release";
#endif

    [Fact]
    public async Task Casing_LiveAndCachedProvidersPreserveOperatingSystemContracts()
    {
        CasingContractResult result = await RunScenarioAsync<CasingContractResult>(
            EnvironmentVariablesConfigurationTestContracts.CasingScenario);

        Assert.Equal(OperatingSystem.IsWindows(), result.IsWindows);
        AssertLookup(result.SingleVariant, "literalExact", found: true, "upper-value");
        AssertLookup(result.SingleVariant, "liveExact", found: true, "upper-value");
        AssertLookup(result.SingleVariant, "cachedExact", found: true, "upper-value");
        AssertLookup(result.SingleVariant, "cachedWrong", found: true, "upper-value");

        if (result.IsWindows)
        {
            AssertLookup(result.SingleVariant, "literalWrong", found: true, "upper-value");
            AssertLookup(result.SingleVariant, "liveWrong", found: true, "upper-value");

            foreach (string surface in new[] { "literalUpper", "literalLower", "literalWrong", "liveUpper", "liveLower", "liveWrong", "cachedUpper", "cachedLower", "cachedWrong" })
            {
                AssertLookup(result.BothVariants, surface, found: true, "lower-value");
            }
        }
        else
        {
            AssertLookup(result.SingleVariant, "literalWrong", found: false, value: null);
            AssertLookup(result.SingleVariant, "liveWrong", found: false, value: null);
            AssertLookup(result.BothVariants, "literalUpper", found: true, "upper-value");
            AssertLookup(result.BothVariants, "literalLower", found: true, "lower-value");
            AssertLookup(result.BothVariants, "literalWrong", found: false, value: null);
            AssertLookup(result.BothVariants, "liveUpper", found: true, "upper-value");
            AssertLookup(result.BothVariants, "liveLower", found: true, "lower-value");
            AssertLookup(result.BothVariants, "liveWrong", found: false, value: null);

            LookupResult cachedUpper = result.BothVariants["cachedUpper"];
            LookupResult cachedLower = result.BothVariants["cachedLower"];
            LookupResult cachedWrong = result.BothVariants["cachedWrong"];
            Assert.True(cachedUpper.Found);
            Assert.Contains(cachedUpper.Value, new[] { "upper-value", "lower-value" });
            Assert.Equal(cachedUpper, cachedLower);
            Assert.Equal(cachedUpper, cachedWrong);
        }

        Assert.Single(result.LiveEnumeration);
        Assert.Single(result.CachedEnumeration);
        Assert.Contains(result.LiveEnumeration[0].Value, new[] { "upper-value", "lower-value" });
        Assert.Contains(result.CachedEnumeration[0].Value, new[] { "upper-value", "lower-value" });
    }

    [Fact]
    public async Task Hierarchy_IndexerEnumerationAndBindingPreserveNormalizationContracts()
    {
        HierarchyContractResult result = await RunScenarioAsync<HierarchyContractResult>(
            EnvironmentVariablesConfigurationTestContracts.HierarchyScenario);

        AssertLookup(result.Literal, "doubleOnlyDouble", found: true, "double-only");
        AssertLookup(result.Literal, "doubleOnlyColon", found: false, value: null);
        AssertLookup(result.Literal, "colonOnlyColon", found: true, "colon-only");
        AssertLookup(result.Literal, "colonOnlyDouble", found: false, value: null);
        AssertLookup(result.Literal, "conflictColon", found: true, "colon-conflict");
        AssertLookup(result.Literal, "conflictDouble", found: true, "double-conflict");

        AssertLookup(result.InitialLive, "doubleOnlyDouble", found: true, "double-only");
        AssertLookup(result.InitialLive, "doubleOnlyColon", found: true, "double-only");
        AssertLookup(result.InitialLive, "colonOnlyColon", found: true, "colon-only");
        AssertLookup(result.InitialLive, "colonOnlyDouble", found: false, value: null);
        AssertLookup(result.InitialLive, "conflictColon", found: true, "colon-conflict");
        AssertLookup(result.InitialLive, "conflictDouble", found: true, "double-conflict");

        AssertLookup(result.InitialCached, "doubleOnlyDouble", found: false, value: null);
        AssertLookup(result.InitialCached, "doubleOnlyColon", found: true, "double-only");
        AssertLookup(result.InitialCached, "colonOnlyColon", found: true, "colon-only");
        AssertLookup(result.InitialCached, "colonOnlyDouble", found: false, value: null);
        AssertLookupValueIsOneOf(result.InitialCached, "conflictColon", "colon-conflict", "double-conflict");
        AssertLookup(result.InitialCached, "conflictDouble", found: false, value: null);

        Assert.Equal("double-only", result.InitialLiveOptions.DoubleOnly);
        Assert.Equal("colon-only", result.InitialLiveOptions.ColonOnly);
        Assert.Equal("colon-conflict", result.InitialLiveOptions.Conflict);
        Assert.Equal("double-only", result.InitialCachedOptions.DoubleOnly);
        Assert.Equal("colon-only", result.InitialCachedOptions.ColonOnly);
        Assert.Contains(result.InitialCachedOptions.Conflict, new[] { "colon-conflict", "double-conflict" });
        AssertNormalizedHierarchyEnumeration(result.InitialLiveEnumeration, expectedCount: 3, includesAfterLoad: false);
        AssertNormalizedHierarchyEnumeration(result.InitialCachedEnumeration, expectedCount: 3, includesAfterLoad: false);

        AssertLookup(result.AfterMutationLive, "afterLoadColon", found: true, "after-load");
        AssertLookup(result.AfterMutationCached, "afterLoadColon", found: false, value: null);
        Assert.Equal("after-load", result.AfterMutationLiveOptions.AfterLoad);
        Assert.Null(result.AfterMutationCachedOptions.AfterLoad);
        Assert.False(result.AfterMutationLiveDictionary.ContainsKey("AfterLoad"));
        Assert.False(result.AfterMutationCachedDictionary.ContainsKey("AfterLoad"));
        AssertNormalizedHierarchyEnumeration(result.AfterMutationLiveEnumeration, expectedCount: 3, includesAfterLoad: false);
        AssertNormalizedHierarchyEnumeration(result.AfterMutationCachedEnumeration, expectedCount: 3, includesAfterLoad: false);

        AssertLookup(result.AfterReloadLive, "afterLoadColon", found: true, "after-load");
        AssertLookup(result.AfterReloadCached, "afterLoadColon", found: true, "after-load");
        Assert.Equal("after-load", result.AfterReloadLiveOptions.AfterLoad);
        Assert.Equal("after-load", result.AfterReloadCachedOptions.AfterLoad);
        Assert.Equal("after-load", result.AfterReloadLiveDictionary["AfterLoad"]);
        Assert.Equal("after-load", result.AfterReloadCachedDictionary["AfterLoad"]);
        AssertNormalizedHierarchyEnumeration(result.AfterReloadLiveEnumeration, expectedCount: 4, includesAfterLoad: true);
        AssertNormalizedHierarchyEnumeration(result.AfterReloadCachedEnumeration, expectedCount: 4, includesAfterLoad: true);
    }

    [Fact]
    public async Task Values_MissingEmptyWhitespaceAndBooleanBindingRemainDistinct()
    {
        ValueStatesContractResult result = await RunScenarioAsync<ValueStatesContractResult>(
            EnvironmentVariablesConfigurationTestContracts.ValuesScenario);
        Dictionary<string, ValueStateResult> values = result.Values.ToDictionary(item => item.Name, StringComparer.Ordinal);

        AssertValue(values["missing"], found: false, value: null);
        AssertValue(values["deleted"], found: false, value: null);
        AssertValue(values["empty"], found: true, string.Empty);
        AssertValue(values["whitespace"], found: true, " \t ");
        AssertValue(values["true"], found: true, "TrUe");
        AssertValue(values["false"], found: true, "FALSE");
        AssertValue(values["numeric"], found: true, "1");
        AssertValue(values["invalid"], found: true, "not-a-boolean");

        AssertBooleanBinding(values["missing"], succeeded: true, value: null);
        AssertBooleanBinding(values["deleted"], succeeded: true, value: null);
        AssertBooleanBinding(values["empty"], succeeded: true, value: null);
        AssertBooleanBinding(values["true"], succeeded: true, value: true);
        AssertBooleanBinding(values["false"], succeeded: true, value: false);
        AssertBooleanBinding(values["whitespace"], succeeded: false, value: null);
        AssertBooleanBinding(values["numeric"], succeeded: false, value: null);
        AssertBooleanBinding(values["invalid"], succeeded: false, value: null);
    }

    [Fact]
    public async Task ConnectionStrings_AllRecognizedPrefixesPreserveLiveAndCachedInversion()
    {
        ConnectionStringContractResult[] result = await RunScenarioAsync<ConnectionStringContractResult[]>(
            EnvironmentVariablesConfigurationTestContracts.ConnectionStringsScenario);
        Dictionary<string, (bool PreservesRaw, string ProviderName)> expected = new(StringComparer.Ordinal)
        {
            ["MYSQLCONNSTR_"] = (false, "MySql.Data.MySqlClient"),
            ["SQLAZURECONNSTR_"] = (false, "System.Data.SqlClient"),
            ["SQLCONNSTR_"] = (false, "System.Data.SqlClient"),
            ["CUSTOMCONNSTR_"] = (false, null),
            ["POSTGRESQLCONNSTR_"] = (true, "Npgsql"),
            ["APIHUBCONNSTR_"] = (true, null),
            ["DOCDBCONNSTR_"] = (true, null),
            ["EVENTHUBCONNSTR_"] = (true, null),
            ["NOTIFICATIONHUBCONNSTR_"] = (true, null),
            ["REDISCACHECONNSTR_"] = (true, null),
            ["SERVICEBUSCONNSTR_"] = (true, null),
        };

        Assert.Equal(expected.Count, result.Length);
        for (int i = 0; i < result.Length; i++)
        {
            ConnectionStringContractResult item = result[i];
            (bool preservesRaw, string providerName) = expected[item.Prefix];
            Assert.Equal(preservesRaw, item.PreservesRawInCachedData);
            Assert.Equal(providerName, item.ExpectedProviderName);
            Assert.Equal(
                i % 2 == 0 ? item.Prefix : item.Prefix.ToLowerInvariant(),
                item.EnvironmentKey[..item.Prefix.Length]);

            AssertLookup(item.LiveRaw, found: true, item.Value);
            AssertLookup(item.LiveMapped, found: false, value: null);
            AssertLookup(item.CachedRaw, found: preservesRaw, preservesRaw ? item.Value : null);
            AssertLookup(item.CachedMapped, found: true, item.Value);

            AssertLookup(item.LiveRawEnumeration, found: preservesRaw, preservesRaw ? item.Value : null);
            AssertLookup(item.LiveMappedEnumeration, found: true, value: null);
            AssertLookup(item.CachedRawEnumeration, found: preservesRaw, preservesRaw ? item.Value : null);
            AssertLookup(item.CachedMappedEnumeration, found: true, item.Value);

            if (providerName is null)
            {
                AssertLookup(item.LiveProviderName, found: false, value: null);
                AssertLookup(item.CachedProviderName, found: false, value: null);
                AssertLookup(item.LiveProviderNameEnumeration, found: false, value: null);
                AssertLookup(item.CachedProviderNameEnumeration, found: false, value: null);
            }
            else
            {
                AssertLookup(item.LiveProviderName, found: false, value: null);
                AssertLookup(item.CachedProviderName, found: true, providerName);
                AssertLookup(item.LiveProviderNameEnumeration, found: true, value: null);
                AssertLookup(item.CachedProviderNameEnumeration, found: true, providerName);
            }
        }
    }

    [Fact]
    public async Task Mutation_LiveCachedReloadAndProviderOrderingRemainDistinct()
    {
        MutationContractResult result = await RunScenarioAsync<MutationContractResult>(
            EnvironmentVariablesConfigurationTestContracts.MutationScenario);

        AssertMutationPhase(result.InitialLive, "initial-env", "lower-fallback", added: null);
        AssertMutationPhase(result.InitialCached, "initial-env", "lower-fallback", added: null);
        AssertMutationPhase(result.AfterMutationLive, "after-load", "environment-override", "added-after-load");
        AssertMutationPhase(result.AfterMutationCached, "initial-env", "lower-fallback", added: null);
        Assert.False(result.LiveAddedKeyEnumeratedBeforeReload);
        Assert.False(result.CachedAddedKeyEnumeratedBeforeReload);

        AssertMutationPhase(result.AfterReloadLive, "after-load", "environment-override", "added-after-load");
        AssertMutationPhase(result.AfterReloadCached, "after-load", "environment-override", "added-after-load");
        Assert.True(result.LiveAddedKeyEnumeratedAfterReload);
        Assert.True(result.CachedAddedKeyEnumeratedAfterReload);

        AssertMutationPhase(result.AfterSecondMutationLive, "after-reload-mutation", "environment-override", "added-after-load");
        AssertMutationPhase(result.AfterSecondMutationCached, "after-load", "environment-override", "added-after-load");
        AssertLookup(result.LiteralAfterDeletion, found: false, value: null);
        AssertMutationPhase(result.AfterDeletionLive, "lower-preload", "environment-override", "added-after-load");
        AssertMutationPhase(result.AfterDeletionCached, "after-load", "environment-override", "added-after-load");
        AssertMutationPhase(result.AfterDeletionReloadLive, "lower-preload", "environment-override", "added-after-load");
        AssertMutationPhase(result.AfterDeletionReloadCached, "lower-preload", "environment-override", "added-after-load");
    }

    [Fact]
    public async Task SpecializationMutation_PreservesPreReloadReadsAndChildProcessInheritance()
    {
        SpecializationMutationContractResult result =
            await RunScenarioAsync<SpecializationMutationContractResult>(
                EnvironmentVariablesConfigurationTestContracts.SpecializationMutationScenario);

        AssertLookup(result.LiteralBeforeReload, found: true, "assigned-value");
        AssertLookup(result.LiveBeforeReload, found: true, "assigned-value");
        AssertLookup(result.CachedBeforeReload, found: false, value: null);
        AssertLookup(result.LiveEnumerationBeforeReload, found: false, value: null);
        AssertLookup(result.CachedEnumerationBeforeReload, found: false, value: null);
        AssertLookup(result.ChildStartedBeforeMutation, found: false, value: null);
        AssertLookup(result.ChildStartedAfterMutation, found: true, "assigned-value");
        AssertLookup(result.LiveAfterReload, found: true, "assigned-value");
        AssertLookup(result.CachedWithoutReload, found: false, value: null);
        AssertLookup(result.LiveEnumerationAfterReload, found: true, "assigned-value");
        AssertLookup(result.CachedEnumerationWithoutReload, found: false, value: null);
    }

    [Fact]
    public async Task Set_LiveProviderMutatesProcessWhileCachedProviderMutatesOnlyData()
    {
        ProviderSetContractResult result = await RunScenarioAsync<ProviderSetContractResult>(
            EnvironmentVariablesConfigurationTestContracts.ProviderSetScenario);

        AssertLookup(result.Live, "valueLiteral", found: true, "live-value");
        AssertLookup(result.Live, "valueProvider", found: true, "live-value");
        AssertLookup(result.Live, "emptyLiteral", found: true, string.Empty);
        AssertLookup(result.Live, "emptyProvider", found: true, string.Empty);
        AssertLookup(result.Live, "deletedLiteral", found: false, value: null);
        AssertLookup(result.Live, "deletedProvider", found: false, value: null);

        AssertLookup(result.Cached, "valueLiteral", found: false, value: null);
        AssertLookup(result.Cached, "valueProvider", found: true, "cached-value");
        AssertLookup(result.Cached, "nullLiteral", found: false, value: null);
        AssertLookup(result.Cached, "nullProvider", found: true, value: null);

        AssertLookup(result.RootLive, "literal", found: false, value: null);
        AssertLookup(result.RootLive, "lowerProvider", found: true, value: null);
        AssertLookup(result.RootLive, "environmentProvider", found: false, value: null);
        AssertLookup(result.RootLive, "root", found: false, value: null);
        AssertLookup(result.RootLive, "enumeration", found: true, value: null);

        AssertLookup(result.RootCached, "literal", found: true, "cached-process-value");
        AssertLookup(result.RootCached, "lowerProvider", found: true, value: null);
        AssertLookup(result.RootCached, "environmentProvider", found: true, value: null);
        AssertLookup(result.RootCached, "root", found: false, value: null);
        AssertLookup(result.RootCached, "enumeration", found: true, value: null);
    }

    private static void AssertValue(ValueStateResult result, bool found, string value)
    {
        AssertLookup(result.Literal, found, value);
        AssertLookup(result.Live, found, value);
        AssertLookup(result.Cached, found, value);
    }

    private static void AssertBooleanBinding(ValueStateResult result, bool succeeded, bool? value)
    {
        Assert.Equal(succeeded, result.LiveBoolean.Succeeded);
        Assert.Equal(succeeded, result.CachedBoolean.Succeeded);
        Assert.Equal(value, result.LiveBoolean.Value);
        Assert.Equal(value, result.CachedBoolean.Value);

        string expectedExceptionType = succeeded ? null : typeof(InvalidOperationException).FullName;
        Assert.Equal(expectedExceptionType, result.LiveBoolean.ExceptionType);
        Assert.Equal(expectedExceptionType, result.CachedBoolean.ExceptionType);
    }

    private static void AssertMutationPhase(
        Dictionary<string, LookupResult> phase, string preload, string fallback, string added)
    {
        AssertLookup(phase, "preload", found: true, preload);
        AssertLookup(phase, "fallback", found: true, fallback);
        AssertLookup(phase, "added", found: added is not null, added);
    }

    private static void AssertNormalizedHierarchyEnumeration(
        ConfigurationEntry[] entries, int expectedCount, bool includesAfterLoad)
    {
        Assert.Equal(expectedCount, entries.Length);
        Assert.DoesNotContain(entries, entry => entry.Key.Contains("__", StringComparison.Ordinal));
        Assert.Contains(entries, entry => entry.Key.EndsWith(":DoubleOnly", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(entries, entry => entry.Key.EndsWith(":ColonOnly", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(entries, entry => entry.Key.EndsWith(":Conflict", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            includesAfterLoad,
            entries.Any(entry => entry.Key.EndsWith(":AfterLoad", StringComparison.OrdinalIgnoreCase)));
    }

    private static void AssertLookupValueIsOneOf(
        Dictionary<string, LookupResult> lookups, string name, params string[] expected)
    {
        LookupResult lookup = lookups[name];
        Assert.True(lookup.Found);
        Assert.Contains(lookup.Value, expected);
    }

    private static void AssertLookup(
        Dictionary<string, LookupResult> lookups, string name, bool found, string value)
    {
        AssertLookup(lookups[name], found, value);
    }

    private static void AssertLookup(LookupResult lookup, bool found, string value)
    {
        Assert.Equal(found, lookup.Found);
        Assert.Equal(value, lookup.Value);
    }

    private static async Task<T> RunScenarioAsync<T>(
        string scenario, [CallerFilePath] string sourceFilePath = "")
    {
        string repositoryRoot = FindRepositoryRoot(sourceFilePath);
        string testHostProject = Path.Combine(
            repositoryRoot,
            "test",
            "WebJobs.Script.Tests.EnvironmentVariables.TestHost",
            TestHostProjectName);
        string dotnetHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (string.IsNullOrEmpty(dotnetHost))
        {
            dotnetHost = "dotnet";
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = dotnetHost,
            WorkingDirectory = repositoryRoot,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--no-build");
        startInfo.ArgumentList.Add("--no-launch-profile");
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add(BuildConfiguration);
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(testHostProject);
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(scenario);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start the environment-variable contract test host.");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(3));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            string timeoutOutput = await standardOutput;
            string timeoutError = await standardError;
            throw new TimeoutException(
                $"Environment-variable contract scenario '{scenario}' exceeded 3 minutes."
                + $"{Environment.NewLine}{timeoutError}{Environment.NewLine}{timeoutOutput}");
        }

        string output = await standardOutput;
        string error = await standardError;
        Assert.True(
            process.ExitCode == 0,
            $"Environment-variable contract scenario '{scenario}' exited with code {process.ExitCode}.{Environment.NewLine}{error}{Environment.NewLine}{output}");

        string resultLine = output
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .SingleOrDefault(line => line.StartsWith(
                EnvironmentVariablesConfigurationTestContracts.ResultPrefix,
                StringComparison.Ordinal));
        Assert.False(
            string.IsNullOrEmpty(resultLine),
            $"Environment-variable contract scenario '{scenario}' did not emit a result.{Environment.NewLine}{error}{Environment.NewLine}{output}");

        T result = JsonSerializer.Deserialize<T>(
            resultLine[EnvironmentVariablesConfigurationTestContracts.ResultPrefix.Length..]);
        return result
            ?? throw new InvalidOperationException($"Unable to deserialize environment-variable contract scenario '{scenario}'.");
    }

    private static string FindRepositoryRoot(string sourceFilePath)
    {
        return TryFindRepositoryRoot(Path.GetDirectoryName(sourceFilePath))
            ?? TryFindRepositoryRoot(AppContext.BaseDirectory)
            ?? TryFindRepositoryRoot(Directory.GetCurrentDirectory())
            ?? throw new DirectoryNotFoundException("Unable to locate WebJobs.Script.sln.");
    }

    private static string TryFindRepositoryRoot(string startPath)
    {
        if (string.IsNullOrEmpty(startPath))
        {
            return null;
        }

        DirectoryInfo directory = new(startPath);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "WebJobs.Script.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
