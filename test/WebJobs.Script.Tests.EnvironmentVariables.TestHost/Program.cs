// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Azure.WebJobs.Script.Config.Tests;

internal static class Program
{
    private static readonly ConnectionStringPrefixDescriptor[] ConnectionStringPrefixes =
    [
        new("MYSQLCONNSTR_", PreservesRawInCachedData: false, "MySql.Data.MySqlClient"),
        new("SQLAZURECONNSTR_", PreservesRawInCachedData: false, "System.Data.SqlClient"),
        new("SQLCONNSTR_", PreservesRawInCachedData: false, "System.Data.SqlClient"),
        new("CUSTOMCONNSTR_", PreservesRawInCachedData: false, ProviderName: null),
        new("POSTGRESQLCONNSTR_", PreservesRawInCachedData: true, "Npgsql"),
        new("APIHUBCONNSTR_", PreservesRawInCachedData: true, ProviderName: null),
        new("DOCDBCONNSTR_", PreservesRawInCachedData: true, ProviderName: null),
        new("EVENTHUBCONNSTR_", PreservesRawInCachedData: true, ProviderName: null),
        new("NOTIFICATIONHUBCONNSTR_", PreservesRawInCachedData: true, ProviderName: null),
        new("REDISCACHECONNSTR_", PreservesRawInCachedData: true, ProviderName: null),
        new("SERVICEBUSCONNSTR_", PreservesRawInCachedData: true, ProviderName: null),
    ];

    public static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            throw new ArgumentException("Exactly one contract scenario is required.", nameof(args));
        }

        object result = args[0] switch
        {
            EnvironmentVariablesConfigurationTestContracts.CasingScenario => RunCasingContract(),
            EnvironmentVariablesConfigurationTestContracts.HierarchyScenario => RunHierarchyContract(),
            EnvironmentVariablesConfigurationTestContracts.ValuesScenario => RunValueStatesContract(),
            EnvironmentVariablesConfigurationTestContracts.ConnectionStringsScenario => RunConnectionStringContract(),
            EnvironmentVariablesConfigurationTestContracts.MutationScenario => RunMutationContract(),
            EnvironmentVariablesConfigurationTestContracts.ProviderSetScenario => RunProviderSetContract(),
            _ => throw new ArgumentOutOfRangeException(nameof(args), args[0], "Unknown contract scenario."),
        };

        Console.WriteLine(
            EnvironmentVariablesConfigurationTestContracts.ResultPrefix
            + JsonSerializer.Serialize(result, result.GetType()));
        return 0;
    }

    private static CasingContractResult RunCasingContract()
    {
        string id = Guid.NewGuid().ToString("N");
        string upperKey = $"AFHOSTCASE{id}".ToUpperInvariant();
        string lowerKey = upperKey.ToLowerInvariant();
        string wrongKey = $"AfHostCase{id.ToUpperInvariant()}";

        Environment.SetEnvironmentVariable(upperKey, "upper-value");
        IConfigurationRoot initialLive = BuildConfiguration(liveEnvironmentLoading: true);
        IConfigurationRoot initialCached = BuildConfiguration(liveEnvironmentLoading: false);
        Dictionary<string, LookupResult> singleVariant = new()
        {
            ["literalExact"] = ReadLiteral(upperKey),
            ["literalWrong"] = ReadLiteral(wrongKey),
            ["liveExact"] = ReadConfiguration(initialLive, upperKey),
            ["liveWrong"] = ReadConfiguration(initialLive, wrongKey),
            ["cachedExact"] = ReadConfiguration(initialCached, upperKey),
            ["cachedWrong"] = ReadConfiguration(initialCached, wrongKey),
        };

        Environment.SetEnvironmentVariable(lowerKey, "lower-value");
        IConfigurationRoot bothLive = BuildConfiguration(liveEnvironmentLoading: true);
        IConfigurationRoot bothCached = BuildConfiguration(liveEnvironmentLoading: false);
        Dictionary<string, LookupResult> bothVariants = new()
        {
            ["literalUpper"] = ReadLiteral(upperKey),
            ["literalLower"] = ReadLiteral(lowerKey),
            ["literalWrong"] = ReadLiteral(wrongKey),
            ["liveUpper"] = ReadConfiguration(bothLive, upperKey),
            ["liveLower"] = ReadConfiguration(bothLive, lowerKey),
            ["liveWrong"] = ReadConfiguration(bothLive, wrongKey),
            ["cachedUpper"] = ReadConfiguration(bothCached, upperKey),
            ["cachedLower"] = ReadConfiguration(bothCached, lowerKey),
            ["cachedWrong"] = ReadConfiguration(bothCached, wrongKey),
        };

        return new CasingContractResult(
            OperatingSystem.IsWindows(),
            singleVariant,
            bothVariants,
            FindEntries(bothLive, upperKey),
            FindEntries(bothCached, upperKey));
    }

    private static HierarchyContractResult RunHierarchyContract()
    {
        string section = $"AFHOSTHIERARCHY{Guid.NewGuid():N}";
        string doubleOnlyDouble = $"{section}__DoubleOnly";
        string doubleOnlyColon = $"{section}:DoubleOnly";
        string colonOnlyColon = $"{section}:ColonOnly";
        string colonOnlyDouble = $"{section}__ColonOnly";
        string conflictColon = $"{section}:Conflict";
        string conflictDouble = $"{section}__Conflict";
        string afterLoadDouble = $"{section}__AfterLoad";
        string afterLoadColon = $"{section}:AfterLoad";

        Environment.SetEnvironmentVariable(doubleOnlyDouble, "double-only");
        Environment.SetEnvironmentVariable(colonOnlyColon, "colon-only");
        Environment.SetEnvironmentVariable(conflictColon, "colon-conflict");
        Environment.SetEnvironmentVariable(conflictDouble, "double-conflict");

        IConfigurationRoot live = BuildConfiguration(liveEnvironmentLoading: true);
        IConfigurationRoot cached = BuildConfiguration(liveEnvironmentLoading: false);
        Dictionary<string, LookupResult> literal = ReadHierarchyLookups(
            ReadLiteral, doubleOnlyDouble, doubleOnlyColon, colonOnlyColon, colonOnlyDouble, conflictColon, conflictDouble);
        Dictionary<string, LookupResult> initialLive = ReadHierarchyLookups(
            key => ReadConfiguration(live, key),
            doubleOnlyDouble,
            doubleOnlyColon,
            colonOnlyColon,
            colonOnlyDouble,
            conflictColon,
            conflictDouble);
        Dictionary<string, LookupResult> initialCached = ReadHierarchyLookups(
            key => ReadConfiguration(cached, key),
            doubleOnlyDouble,
            doubleOnlyColon,
            colonOnlyColon,
            colonOnlyDouble,
            conflictColon,
            conflictDouble);
        HierarchyOptions initialLiveOptions = BindHierarchy(live, section);
        HierarchyOptions initialCachedOptions = BindHierarchy(cached, section);
        ConfigurationEntry[] initialLiveEnumeration = FindSectionEntries(live, section);
        ConfigurationEntry[] initialCachedEnumeration = FindSectionEntries(cached, section);

        Environment.SetEnvironmentVariable(afterLoadDouble, "after-load");
        Dictionary<string, LookupResult> afterMutationLive = new()
        {
            ["afterLoadColon"] = ReadConfiguration(live, afterLoadColon),
        };
        Dictionary<string, LookupResult> afterMutationCached = new()
        {
            ["afterLoadColon"] = ReadConfiguration(cached, afterLoadColon),
        };
        HierarchyOptions afterMutationLiveOptions = BindHierarchy(live, section);
        HierarchyOptions afterMutationCachedOptions = BindHierarchy(cached, section);
        Dictionary<string, string> afterMutationLiveDictionary = BindHierarchyDictionary(live, section);
        Dictionary<string, string> afterMutationCachedDictionary = BindHierarchyDictionary(cached, section);
        ConfigurationEntry[] afterMutationLiveEnumeration = FindSectionEntries(live, section);
        ConfigurationEntry[] afterMutationCachedEnumeration = FindSectionEntries(cached, section);

        live.Reload();
        cached.Reload();

        return new HierarchyContractResult(
            literal,
            initialLive,
            initialCached,
            initialLiveOptions,
            initialCachedOptions,
            initialLiveEnumeration,
            initialCachedEnumeration,
            afterMutationLive,
            afterMutationCached,
            afterMutationLiveOptions,
            afterMutationCachedOptions,
            afterMutationLiveDictionary,
            afterMutationCachedDictionary,
            afterMutationLiveEnumeration,
            afterMutationCachedEnumeration,
            new Dictionary<string, LookupResult>
            {
                ["afterLoadColon"] = ReadConfiguration(live, afterLoadColon),
            },
            new Dictionary<string, LookupResult>
            {
                ["afterLoadColon"] = ReadConfiguration(cached, afterLoadColon),
            },
            BindHierarchy(live, section),
            BindHierarchy(cached, section),
            BindHierarchyDictionary(live, section),
            BindHierarchyDictionary(cached, section),
            FindSectionEntries(live, section),
            FindSectionEntries(cached, section));
    }

    private static ValueStatesContractResult RunValueStatesContract()
    {
        string prefix = $"AFHOSTVALUES{Guid.NewGuid():N}_";
        Dictionary<string, string> keys = new(StringComparer.Ordinal)
        {
            ["missing"] = prefix + "MISSING",
            ["deleted"] = prefix + "DELETED",
            ["empty"] = prefix + "EMPTY",
            ["whitespace"] = prefix + "WHITESPACE",
            ["true"] = prefix + "TRUE",
            ["false"] = prefix + "FALSE",
            ["numeric"] = prefix + "NUMERIC",
            ["invalid"] = prefix + "INVALID",
        };

        Environment.SetEnvironmentVariable(keys["deleted"], "delete-me");
        Environment.SetEnvironmentVariable(keys["deleted"], null);
        Environment.SetEnvironmentVariable(keys["empty"], string.Empty);
        Environment.SetEnvironmentVariable(keys["whitespace"], " \t ");
        Environment.SetEnvironmentVariable(keys["true"], "TrUe");
        Environment.SetEnvironmentVariable(keys["false"], "FALSE");
        Environment.SetEnvironmentVariable(keys["numeric"], "1");
        Environment.SetEnvironmentVariable(keys["invalid"], "not-a-boolean");

        IConfigurationRoot live = BuildConfiguration(liveEnvironmentLoading: true);
        IConfigurationRoot cached = BuildConfiguration(liveEnvironmentLoading: false);
        ValueStateResult[] values = keys
            .Select(pair => new ValueStateResult(
                pair.Key,
                ReadLiteral(pair.Value),
                ReadConfiguration(live, pair.Value),
                ReadConfiguration(cached, pair.Value),
                BindBoolean(live, pair.Value),
                BindBoolean(cached, pair.Value)))
            .ToArray();

        return new ValueStatesContractResult(values);
    }

    private static ConnectionStringContractResult[] RunConnectionStringContract()
    {
        string id = Guid.NewGuid().ToString("N");
        var variables = new List<ConnectionStringVariable>(ConnectionStringPrefixes.Length);

        for (int i = 0; i < ConnectionStringPrefixes.Length; i++)
        {
            ConnectionStringPrefixDescriptor descriptor = ConnectionStringPrefixes[i];
            string environmentPrefix = i % 2 == 0
                ? descriptor.Prefix
                : descriptor.Prefix.ToLowerInvariant();
            string name = $"AFHOST{i}{id}";
            string environmentKey = environmentPrefix + name;
            string value = $"connection-value-{i}";
            Environment.SetEnvironmentVariable(environmentKey, value);
            variables.Add(new ConnectionStringVariable(descriptor, environmentKey, name, value));
        }

        IConfigurationRoot live = BuildConfiguration(liveEnvironmentLoading: true);
        IConfigurationRoot cached = BuildConfiguration(liveEnvironmentLoading: false);

        return variables
            .Select(variable =>
            {
                string mappedKey = $"ConnectionStrings:{variable.Name}";
                string providerNameKey = $"{mappedKey}_ProviderName";
                return new ConnectionStringContractResult(
                    variable.Descriptor.Prefix,
                    variable.EnvironmentKey,
                    variable.Descriptor.PreservesRawInCachedData,
                    variable.Descriptor.ProviderName,
                    variable.Value,
                    ReadConfiguration(live, variable.EnvironmentKey),
                    ReadConfiguration(live, mappedKey),
                    ReadConfiguration(cached, variable.EnvironmentKey),
                    ReadConfiguration(cached, mappedKey),
                    FindEntry(live, variable.EnvironmentKey),
                    FindEntry(live, mappedKey),
                    FindEntry(cached, variable.EnvironmentKey),
                    FindEntry(cached, mappedKey),
                    ReadConfiguration(live, providerNameKey),
                    ReadConfiguration(cached, providerNameKey),
                    FindEntry(live, providerNameKey),
                    FindEntry(cached, providerNameKey));
            })
            .ToArray();
    }

    private static MutationContractResult RunMutationContract()
    {
        string prefix = $"AFHOSTMUTATION{Guid.NewGuid():N}_";
        string preloadKey = prefix + "PRELOAD";
        string fallbackKey = prefix + "FALLBACK";
        string addedKey = prefix + "ADDED";
        var lowerValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [preloadKey] = "lower-preload",
            [fallbackKey] = "lower-fallback",
        };

        Environment.SetEnvironmentVariable(preloadKey, "initial-env");
        IConfigurationRoot live = BuildConfiguration(liveEnvironmentLoading: true, lowerValues);
        IConfigurationRoot cached = BuildConfiguration(liveEnvironmentLoading: false, lowerValues);
        Dictionary<string, LookupResult> initialLive = ReadMutationLookups(live, preloadKey, fallbackKey, addedKey);
        Dictionary<string, LookupResult> initialCached = ReadMutationLookups(cached, preloadKey, fallbackKey, addedKey);

        Environment.SetEnvironmentVariable(preloadKey, "after-load");
        Environment.SetEnvironmentVariable(fallbackKey, "environment-override");
        Environment.SetEnvironmentVariable(addedKey, "added-after-load");
        Dictionary<string, LookupResult> afterMutationLive = ReadMutationLookups(live, preloadKey, fallbackKey, addedKey);
        Dictionary<string, LookupResult> afterMutationCached = ReadMutationLookups(cached, preloadKey, fallbackKey, addedKey);
        bool liveAddedKeyEnumeratedBeforeReload = FindEntry(live, addedKey).Found;
        bool cachedAddedKeyEnumeratedBeforeReload = FindEntry(cached, addedKey).Found;

        live.Reload();
        cached.Reload();
        Dictionary<string, LookupResult> afterReloadLive = ReadMutationLookups(live, preloadKey, fallbackKey, addedKey);
        Dictionary<string, LookupResult> afterReloadCached = ReadMutationLookups(cached, preloadKey, fallbackKey, addedKey);
        bool liveAddedKeyEnumeratedAfterReload = FindEntry(live, addedKey).Found;
        bool cachedAddedKeyEnumeratedAfterReload = FindEntry(cached, addedKey).Found;

        Environment.SetEnvironmentVariable(preloadKey, "after-reload-mutation");
        Dictionary<string, LookupResult> afterSecondMutationLive = ReadMutationLookups(live, preloadKey, fallbackKey, addedKey);
        Dictionary<string, LookupResult> afterSecondMutationCached = ReadMutationLookups(cached, preloadKey, fallbackKey, addedKey);

        Environment.SetEnvironmentVariable(preloadKey, null);
        LookupResult literalAfterDeletion = ReadLiteral(preloadKey);
        Dictionary<string, LookupResult> afterDeletionLive = ReadMutationLookups(live, preloadKey, fallbackKey, addedKey);
        Dictionary<string, LookupResult> afterDeletionCached = ReadMutationLookups(cached, preloadKey, fallbackKey, addedKey);

        live.Reload();
        cached.Reload();

        return new MutationContractResult(
            initialLive,
            initialCached,
            afterMutationLive,
            afterMutationCached,
            liveAddedKeyEnumeratedBeforeReload,
            cachedAddedKeyEnumeratedBeforeReload,
            afterReloadLive,
            afterReloadCached,
            liveAddedKeyEnumeratedAfterReload,
            cachedAddedKeyEnumeratedAfterReload,
            afterSecondMutationLive,
            afterSecondMutationCached,
            literalAfterDeletion,
            afterDeletionLive,
            afterDeletionCached,
            ReadMutationLookups(live, preloadKey, fallbackKey, addedKey),
            ReadMutationLookups(cached, preloadKey, fallbackKey, addedKey));
    }

    private static ProviderSetContractResult RunProviderSetContract()
    {
        string prefix = $"AFHOSTSET{Guid.NewGuid():N}_";
        string liveKey = prefix + "LIVE";
        string cachedKey = prefix + "CACHED";
        string rootLiveKey = prefix + "ROOTLIVE";
        string rootCachedKey = prefix + "ROOTCACHED";
        IConfigurationRoot liveConfiguration = BuildConfiguration(liveEnvironmentLoading: true);
        IConfigurationRoot cachedConfiguration = BuildConfiguration(liveEnvironmentLoading: false);
        IConfigurationProvider liveProvider = liveConfiguration.Providers.Last();
        IConfigurationProvider cachedProvider = cachedConfiguration.Providers.Last();

        liveProvider.Set(liveKey, "live-value");
        LookupResult liveValueLiteral = ReadLiteral(liveKey);
        LookupResult liveValueProvider = ReadProvider(liveProvider, liveKey);
        liveProvider.Set(liveKey, string.Empty);
        LookupResult liveEmptyLiteral = ReadLiteral(liveKey);
        LookupResult liveEmptyProvider = ReadProvider(liveProvider, liveKey);
        liveProvider.Set(liveKey, null);

        cachedProvider.Set(cachedKey, "cached-value");
        LookupResult cachedValueLiteral = ReadLiteral(cachedKey);
        LookupResult cachedValueProvider = ReadProvider(cachedProvider, cachedKey);
        cachedProvider.Set(cachedKey, null);

        Environment.SetEnvironmentVariable(rootLiveKey, "live-process-value");
        IConfigurationRoot rootLiveConfiguration = BuildConfiguration(
            liveEnvironmentLoading: true,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [rootLiveKey] = "lower-live-value",
            });
        IConfigurationProvider[] rootLiveProviders = rootLiveConfiguration.Providers.ToArray();
        rootLiveConfiguration[rootLiveKey] = null;

        Environment.SetEnvironmentVariable(rootCachedKey, "cached-process-value");
        IConfigurationRoot rootCachedConfiguration = BuildConfiguration(
            liveEnvironmentLoading: false,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [rootCachedKey] = "lower-cached-value",
            });
        IConfigurationProvider[] rootCachedProviders = rootCachedConfiguration.Providers.ToArray();
        rootCachedConfiguration[rootCachedKey] = null;

        return new ProviderSetContractResult(
            new Dictionary<string, LookupResult>
            {
                ["valueLiteral"] = liveValueLiteral,
                ["valueProvider"] = liveValueProvider,
                ["emptyLiteral"] = liveEmptyLiteral,
                ["emptyProvider"] = liveEmptyProvider,
                ["deletedLiteral"] = ReadLiteral(liveKey),
                ["deletedProvider"] = ReadProvider(liveProvider, liveKey),
            },
            new Dictionary<string, LookupResult>
            {
                ["valueLiteral"] = cachedValueLiteral,
                ["valueProvider"] = cachedValueProvider,
                ["nullLiteral"] = ReadLiteral(cachedKey),
                ["nullProvider"] = ReadProvider(cachedProvider, cachedKey),
            },
            new Dictionary<string, LookupResult>
            {
                ["literal"] = ReadLiteral(rootLiveKey),
                ["lowerProvider"] = ReadProvider(rootLiveProviders[0], rootLiveKey),
                ["environmentProvider"] = ReadProvider(rootLiveProviders[1], rootLiveKey),
                ["root"] = ReadConfiguration(rootLiveConfiguration, rootLiveKey),
                ["enumeration"] = FindEntry(rootLiveConfiguration, rootLiveKey),
            },
            new Dictionary<string, LookupResult>
            {
                ["literal"] = ReadLiteral(rootCachedKey),
                ["lowerProvider"] = ReadProvider(rootCachedProviders[0], rootCachedKey),
                ["environmentProvider"] = ReadProvider(rootCachedProviders[1], rootCachedKey),
                ["root"] = ReadConfiguration(rootCachedConfiguration, rootCachedKey),
                ["enumeration"] = FindEntry(rootCachedConfiguration, rootCachedKey),
            });
    }

    private static IConfigurationRoot BuildConfiguration(
        bool liveEnvironmentLoading, Dictionary<string, string> lowerValues = null)
    {
        ConfigurationBuilder builder = new();
        if (lowerValues is not null)
        {
            builder.AddInMemoryCollection(lowerValues);
        }

        builder.Add(new ScriptEnvironmentVariablesConfigurationSource(liveEnvironmentLoading));
        return builder.Build();
    }

    private static Dictionary<string, LookupResult> ReadHierarchyLookups(
        Func<string, LookupResult> read,
        string doubleOnlyDouble,
        string doubleOnlyColon,
        string colonOnlyColon,
        string colonOnlyDouble,
        string conflictColon,
        string conflictDouble)
    {
        return new Dictionary<string, LookupResult>
        {
            ["doubleOnlyDouble"] = read(doubleOnlyDouble),
            ["doubleOnlyColon"] = read(doubleOnlyColon),
            ["colonOnlyColon"] = read(colonOnlyColon),
            ["colonOnlyDouble"] = read(colonOnlyDouble),
            ["conflictColon"] = read(conflictColon),
            ["conflictDouble"] = read(conflictDouble),
        };
    }

    private static Dictionary<string, LookupResult> ReadMutationLookups(
        IConfiguration configuration, string preloadKey, string fallbackKey, string addedKey)
    {
        return new Dictionary<string, LookupResult>
        {
            ["preload"] = ReadConfiguration(configuration, preloadKey),
            ["fallback"] = ReadConfiguration(configuration, fallbackKey),
            ["added"] = ReadConfiguration(configuration, addedKey),
        };
    }

    private static HierarchyOptions BindHierarchy(IConfiguration configuration, string section)
    {
        return configuration.GetSection(section).Get<HierarchyOptions>() ?? new HierarchyOptions();
    }

    private static Dictionary<string, string> BindHierarchyDictionary(
        IConfiguration configuration, string section)
    {
        return configuration.GetSection(section).Get<Dictionary<string, string>>()
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static BooleanBindingResult BindBoolean(IConfiguration configuration, string key)
    {
        try
        {
            return new BooleanBindingResult(
                Succeeded: true,
                configuration.GetValue<bool?>(key),
                ExceptionType: null);
        }
        catch (InvalidOperationException exception)
        {
            return new BooleanBindingResult(
                Succeeded: false,
                Value: null,
                exception.GetType().FullName);
        }
    }

    private static LookupResult ReadLiteral(string key)
    {
        string value = Environment.GetEnvironmentVariable(key);
        return new LookupResult(value is not null, value);
    }

    private static LookupResult ReadConfiguration(IConfiguration configuration, string key)
    {
        string value = configuration[key];
        return new LookupResult(value is not null, value);
    }

    private static LookupResult ReadProvider(IConfigurationProvider provider, string key)
    {
        bool found = provider.TryGet(key, out string value);
        return new LookupResult(found, value);
    }

    private static LookupResult FindEntry(IConfiguration configuration, string key)
    {
        foreach (KeyValuePair<string, string> entry in configuration.AsEnumerable())
        {
            if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return new LookupResult(Found: true, entry.Value);
            }
        }

        return new LookupResult(Found: false, Value: null);
    }

    private static ConfigurationEntry[] FindEntries(IConfiguration configuration, string key)
    {
        return configuration
            .AsEnumerable()
            .Where(entry => string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
            .Select(entry => new ConfigurationEntry(entry.Key, entry.Value))
            .ToArray();
    }

    private static ConfigurationEntry[] FindSectionEntries(IConfiguration configuration, string section)
    {
        string prefix = section + ConfigurationPath.KeyDelimiter;
        return configuration
            .AsEnumerable()
            .Where(entry => entry.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(entry => new ConfigurationEntry(entry.Key, entry.Value))
            .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private sealed record ConnectionStringPrefixDescriptor(
        string Prefix, bool PreservesRawInCachedData, string ProviderName);

    private sealed record ConnectionStringVariable(
        ConnectionStringPrefixDescriptor Descriptor, string EnvironmentKey, string Name, string Value);
}
