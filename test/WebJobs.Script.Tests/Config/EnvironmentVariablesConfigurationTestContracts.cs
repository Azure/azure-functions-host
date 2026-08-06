// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.Config.Tests;

internal static class EnvironmentVariablesConfigurationTestContracts
{
    public const string ResultPrefix = "ENVIRONMENT_CONTRACT_RESULT:";
    public const string CasingScenario = "casing";
    public const string HierarchyScenario = "hierarchy";
    public const string ValuesScenario = "values";
    public const string ConnectionStringsScenario = "connection-strings";
    public const string MutationScenario = "mutation";
    public const string ProviderSetScenario = "provider-set";
}

internal sealed record LookupResult(bool Found, string Value);

internal sealed record ConfigurationEntry(string Key, string Value);

internal sealed record BooleanBindingResult(bool Succeeded, bool? Value, string ExceptionType);

internal sealed record CasingContractResult(
    bool IsWindows,
    Dictionary<string, LookupResult> SingleVariant,
    Dictionary<string, LookupResult> BothVariants,
    ConfigurationEntry[] LiveEnumeration,
    ConfigurationEntry[] CachedEnumeration);

internal sealed class HierarchyOptions
{
    public string DoubleOnly { get; set; }

    public string ColonOnly { get; set; }

    public string Conflict { get; set; }

    public string AfterLoad { get; set; }
}

internal sealed record HierarchyContractResult(
    Dictionary<string, LookupResult> Literal,
    Dictionary<string, LookupResult> InitialLive,
    Dictionary<string, LookupResult> InitialCached,
    HierarchyOptions InitialLiveOptions,
    HierarchyOptions InitialCachedOptions,
    ConfigurationEntry[] InitialLiveEnumeration,
    ConfigurationEntry[] InitialCachedEnumeration,
    Dictionary<string, LookupResult> AfterMutationLive,
    Dictionary<string, LookupResult> AfterMutationCached,
    HierarchyOptions AfterMutationLiveOptions,
    HierarchyOptions AfterMutationCachedOptions,
    Dictionary<string, string> AfterMutationLiveDictionary,
    Dictionary<string, string> AfterMutationCachedDictionary,
    ConfigurationEntry[] AfterMutationLiveEnumeration,
    ConfigurationEntry[] AfterMutationCachedEnumeration,
    Dictionary<string, LookupResult> AfterReloadLive,
    Dictionary<string, LookupResult> AfterReloadCached,
    HierarchyOptions AfterReloadLiveOptions,
    HierarchyOptions AfterReloadCachedOptions,
    Dictionary<string, string> AfterReloadLiveDictionary,
    Dictionary<string, string> AfterReloadCachedDictionary,
    ConfigurationEntry[] AfterReloadLiveEnumeration,
    ConfigurationEntry[] AfterReloadCachedEnumeration);

internal sealed record ValueStateResult(
    string Name,
    LookupResult Literal,
    LookupResult Live,
    LookupResult Cached,
    BooleanBindingResult LiveBoolean,
    BooleanBindingResult CachedBoolean);

internal sealed record ValueStatesContractResult(ValueStateResult[] Values);

internal sealed record ConnectionStringContractResult(
    string Prefix,
    string EnvironmentKey,
    bool PreservesRawInCachedData,
    string ExpectedProviderName,
    string Value,
    LookupResult LiveRaw,
    LookupResult LiveMapped,
    LookupResult CachedRaw,
    LookupResult CachedMapped,
    LookupResult LiveRawEnumeration,
    LookupResult LiveMappedEnumeration,
    LookupResult CachedRawEnumeration,
    LookupResult CachedMappedEnumeration,
    LookupResult LiveProviderName,
    LookupResult CachedProviderName,
    LookupResult LiveProviderNameEnumeration,
    LookupResult CachedProviderNameEnumeration);

internal sealed record MutationContractResult(
    Dictionary<string, LookupResult> InitialLive,
    Dictionary<string, LookupResult> InitialCached,
    Dictionary<string, LookupResult> AfterMutationLive,
    Dictionary<string, LookupResult> AfterMutationCached,
    bool LiveAddedKeyEnumeratedBeforeReload,
    bool CachedAddedKeyEnumeratedBeforeReload,
    Dictionary<string, LookupResult> AfterReloadLive,
    Dictionary<string, LookupResult> AfterReloadCached,
    bool LiveAddedKeyEnumeratedAfterReload,
    bool CachedAddedKeyEnumeratedAfterReload,
    Dictionary<string, LookupResult> AfterSecondMutationLive,
    Dictionary<string, LookupResult> AfterSecondMutationCached,
    LookupResult LiteralAfterDeletion,
    Dictionary<string, LookupResult> AfterDeletionLive,
    Dictionary<string, LookupResult> AfterDeletionCached,
    Dictionary<string, LookupResult> AfterDeletionReloadLive,
    Dictionary<string, LookupResult> AfterDeletionReloadCached);

internal sealed record ProviderSetContractResult(
    Dictionary<string, LookupResult> Live,
    Dictionary<string, LookupResult> Cached,
    Dictionary<string, LookupResult> RootLive,
    Dictionary<string, LookupResult> RootCached);
