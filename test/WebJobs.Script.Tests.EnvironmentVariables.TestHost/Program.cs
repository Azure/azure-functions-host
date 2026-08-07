// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Tests;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
        if (args.Length >= 1
            && string.Equals(
                args[0],
                EnvironmentBehaviorParityTestContracts.StaticCacheScenario,
                StringComparison.Ordinal))
        {
            string[] values = args.Length == 2
                ? args[1].Split(',')
                : ["false", "false"];
            WriteResult(RunStaticCacheContract(
                bool.Parse(values[0]),
                bool.Parse(values[1])));
            return 0;
        }

        if (args.Length == 1
            && string.Equals(
                args[0],
                EnvironmentBehaviorParityTestContracts.HelperMatrixScenario,
                StringComparison.Ordinal))
        {
            WriteResult(RunEnvironmentHelperMatrix());
            return 0;
        }

        if (args.Length == 1
            && string.Equals(
                args[0],
                EnvironmentBehaviorParityTestContracts.JwtLatchScenario,
                StringComparison.Ordinal))
        {
            WriteResult(RunJwtLatchContract());
            return 0;
        }

        if (args.Length == 2
            && string.Equals(
                args[0],
                EnvironmentBehaviorParityTestContracts.WebScriptHostConfigurationScenario,
                StringComparison.Ordinal))
        {
            string[] values = args[1].Split(',');
            WriteResult(RunWebScriptHostConfigurationContract(
                bool.Parse(values[0]),
                bool.Parse(values[1]),
                bool.Parse(values[2])));
            return 0;
        }

        if (args.Length == 2
            && string.Equals(
                args[0],
                EnvironmentVariablesConfigurationTestContracts.ChildProcessReadScenario,
                StringComparison.Ordinal))
        {
            _ = Console.ReadLine();
            WriteResult(ReadLiteral(args[1]));
            return 0;
        }

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
            EnvironmentVariablesConfigurationTestContracts.SpecializationMutationScenario => RunSpecializationMutationContract(),
            _ => throw new ArgumentOutOfRangeException(nameof(args), args[0], "Unknown contract scenario."),
        };

        WriteResult(result);
        return 0;
    }

    private static EnvironmentHelperMatrixResult RunEnvironmentHelperMatrix()
    {
        Type extensionsType = typeof(SystemEnvironment).Assembly.GetType(
            "Microsoft.Azure.WebJobs.Script.EnvironmentExtensions",
            throwOnError: true);
        MethodInfo[] methods = extensionsType
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .OrderBy(FormatMethodSignature, StringComparer.Ordinal)
            .ToArray();
        MethodInfo clearCache = methods.Single(method => string.Equals(
            method.Name,
            "ClearCache",
            StringComparison.Ordinal));
        MethodInfo[] markerPredicateMethods = methods
            .Where(IsEnvironmentMarkerPredicate)
            .OrderBy(method => method.Name, StringComparer.Ordinal)
            .ToArray();

        EnvironmentProfileResult[] profiles = EnvironmentBehaviorParityFixtures.CompleteProfiles
            .Select(profile => new EnvironmentProfileResult(
                profile.Profile,
                profile.DefaultPlatform,
                Is64BitProcess: true,
                RunHelperSet(
                    profile,
                    profile.DefaultPlatform,
                    is64BitProcess: true,
                    variableOverrides: null,
                    methods,
                    clearCache)))
            .ToArray();
        EnvironmentStableFactVariantResult[] stableFactVariants =
            EnvironmentBehaviorParityFixtures.StableFactVariants
                .Select(variant =>
                {
                    EnvironmentProfileContract profile =
                        EnvironmentBehaviorParityFixtures.CompleteProfiles.Single(
                            candidate => candidate.Profile == variant.Profile);
                    return new EnvironmentStableFactVariantResult(
                        variant.Name,
                        variant.Profile,
                        variant.ProcessFacts.Platform.ToString(),
                        variant.ProcessFacts.Is64BitProcess,
                        RunHelperSet(
                            profile,
                            variant.ProcessFacts.Platform.ToString(),
                            variant.ProcessFacts.Is64BitProcess,
                            variant.VariableOverrides,
                            methods,
                            clearCache));
                })
            .ToArray();

        EnvironmentMarkerObservationResult[] observations =
            EnvironmentBehaviorParityFixtures.MarkerObservations
                .Select(observation =>
                {
                    clearCache.Invoke(null, null);
                    TestEnvironment environment = new(observation.Markers)
                    {
                        Platform = ParsePlatform(observation.Platform)
                    };
                    Dictionary<string, string> predicates = markerPredicateMethods.ToDictionary(
                        method => method.Name,
                        method => InvokeHelper(method, environment).Value,
                        StringComparer.Ordinal);
                    return new EnvironmentMarkerObservationResult(
                        observation.Name,
                        observation.Profile,
                        observation.Evidence,
                        observation.Phase,
                        predicates);
                })
                .ToArray();

        clearCache.Invoke(null, null);
        return new EnvironmentHelperMatrixResult(profiles, stableFactVariants, observations);
    }

    private static EnvironmentHelperResult[] RunHelperSet(
        EnvironmentProfileContract profile,
        string platform,
        bool is64BitProcess,
        IReadOnlyDictionary<string, string> variableOverrides,
        MethodInfo[] methods,
        MethodInfo clearCache)
    {
        clearCache.Invoke(null, null);
        TestEnvironment environment = new(
            EnvironmentBehaviorParityFixtures.CreateVariables(profile),
            is64BitProcess)
        {
            Platform = ParsePlatform(platform)
        };
        if (variableOverrides is not null)
        {
            foreach ((string name, string value) in variableOverrides)
            {
                environment.SetEnvironmentVariable(name, value);
            }
        }

        EnvironmentHelperResult[] helpers = methods
            .Select(method => InvokeHelper(method, environment))
            .ToArray();
        clearCache.Invoke(null, null);

        return helpers;
    }

    private static bool IsEnvironmentMarkerPredicate(MethodInfo method)
    {
        ParameterInfo[] parameters = method.GetParameters();
        return method.ReturnType == typeof(bool)
            && parameters.Length == 1
            && method.IsDefined(typeof(ExtensionAttribute), inherit: false)
            && string.Equals(
                parameters[0].Name,
                "environment",
                StringComparison.Ordinal);
    }

    private static EnvironmentHelperResult InvokeHelper(
        MethodInfo method, TestEnvironment environment)
    {
        object[] arguments = method.GetParameters()
            .Select(parameter => parameter.Name switch
            {
                "environment" => (object)environment,
                "name" => "PARITY_MISSING",
                "defaultValue" => "parity-default",
                "workerRuntime" => null,
                "group" => null,
                _ => throw new InvalidOperationException(
                    $"No parity argument is defined for parameter '{parameter.Name}' on '{method.Name}'.")
            })
            .ToArray();

        try
        {
            object result = method.Invoke(null, arguments);
            string value = FormatHelperValue(method, result, environment);
            if (method.GetParameters().Any(parameter => parameter.IsOut))
            {
                value = $"{value};out={FormatValue(arguments[^1])}";
            }

            return new EnvironmentHelperResult(FormatMethodSignature(method), value);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            Exception inner = exception.InnerException;
            return new EnvironmentHelperResult(
                FormatMethodSignature(method),
                $"exception:{inner.GetType().FullName}:{inner.Message}");
        }
    }

    private static string FormatHelperValue(
        MethodInfo method,
        object value,
        object environment)
    {
        if (string.Equals(method.Name, "GetEffectiveCoresCount", StringComparison.Ordinal)
            && value is int cores
            && (!InvokeEnvironmentBoolean(environment, "IsWindowsConsumption")
                || InvokeEnvironmentBoolean(environment, "IsVMSS"))
            && cores == Environment.ProcessorCount)
        {
            return "$PROCESSOR_COUNT";
        }

        return FormatValue(value);
    }

    private static string FormatValue(object value)
    {
        return value switch
        {
            null => "null",
            bool boolean => boolean ? "true" : "false",
            string text => JsonSerializer.Serialize(text),
            Enum enumValue => enumValue.ToString(),
            IEnumerable<string> strings => JsonSerializer.Serialize(
                strings.OrderBy(item => item, StringComparer.Ordinal)),
            IEnumerable enumerable => JsonSerializer.Serialize(
                enumerable.Cast<object>().Select(FormatValue).ToArray()),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        };
    }

    private static StaticCacheContractResult RunStaticCacheContract(
        bool initialMultiLanguage,
        bool initialApplicationInsights)
    {
        TestEnvironment environment = new();
        environment.SetEnvironmentVariable(
            EnvironmentSettingNames.AzureWebsitePlaceholderMode,
            "1");
        environment.SetEnvironmentVariable(
            EnvironmentSettingNames.AppInsightsAgent,
            initialApplicationInsights.ToString());
        environment.SetEnvironmentVariable(
            EnvironmentSettingNames.AppKind,
            initialMultiLanguage ? "workflowApp" : string.Empty);

        bool placeholderApplicationInsights = InvokeEnvironmentBoolean(
            environment,
            "IsApplicationInsightsAgentEnabled");

        environment.SetEnvironmentVariable(
            EnvironmentSettingNames.AzureWebsitePlaceholderMode,
            "0");
        bool specializedApplicationInsights = InvokeEnvironmentBoolean(
            environment,
            "IsApplicationInsightsAgentEnabled");
        environment.SetEnvironmentVariable(
            EnvironmentSettingNames.AppInsightsAgent,
            (!initialApplicationInsights).ToString());
        bool mutatedApplicationInsights = InvokeEnvironmentBoolean(
            environment,
            "IsApplicationInsightsAgentEnabled");

        bool initialMultiLanguageResult = InvokeEnvironmentBoolean(
            environment,
            "IsMultiLanguageRuntimeEnvironment");
        environment.SetEnvironmentVariable(
            EnvironmentSettingNames.AppKind,
            initialMultiLanguage ? string.Empty : "workflowApp");
        bool mutatedMultiLanguageResult = InvokeEnvironmentBoolean(
            environment,
            "IsMultiLanguageRuntimeEnvironment");

        return new StaticCacheContractResult(
            placeholderApplicationInsights,
            specializedApplicationInsights,
            mutatedApplicationInsights,
            initialMultiLanguageResult,
            mutatedMultiLanguageResult);
    }

    private static JwtLatchContractResult RunJwtLatchContract()
    {
        Environment.SetEnvironmentVariable(
            EnvironmentSettingNames.AzureWebsitePlaceholderMode,
            "1");
        Environment.SetEnvironmentVariable(
            EnvironmentSettingNames.AzureWebsiteSku,
            ScriptConstants.FlexConsumptionSku);
        Environment.SetEnvironmentVariable(
            EnvironmentSettingNames.WebsitePodName,
            "placeholder-pod");
        Environment.SetEnvironmentVariable(
            EnvironmentSettingNames.AzureWebsiteName,
            "placeholder-site");
        Environment.SetEnvironmentVariable(
            EnvironmentSettingNames.ContainerEncryptionKey,
            Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()));

        ServiceCollection services = new();
        services.AddAuthentication().AddScriptJwtBearer();
        using ServiceProvider serviceProvider = services.BuildServiceProvider();
        JwtBearerOptions options = serviceProvider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        string[] placeholderAudiences = options.TokenValidationParameters.ValidAudiences.ToArray();

        Environment.SetEnvironmentVariable(
            EnvironmentSettingNames.AzureWebsitePlaceholderMode,
            "0");
        Environment.SetEnvironmentVariable(
            EnvironmentSettingNames.AzureWebsiteName,
            "specialized-site");
        Environment.SetEnvironmentVariable(
            EnvironmentSettingNames.AzureWebsiteRuntimeSiteName,
            "specialized-runtime");
        InvokeMessageReceived(options, serviceProvider);
        string[] specializedAudiences = options.TokenValidationParameters.ValidAudiences.ToArray();

        Environment.SetEnvironmentVariable(
            EnvironmentSettingNames.AzureWebsiteName,
            "second-site");
        Environment.SetEnvironmentVariable(
            EnvironmentSettingNames.AzureWebsiteRuntimeSiteName,
            "second-runtime");
        InvokeMessageReceived(options, serviceProvider);
        string[] audiencesAfterSecondMutation =
            options.TokenValidationParameters.ValidAudiences.ToArray();

        return new JwtLatchContractResult(
            placeholderAudiences,
            specializedAudiences,
            audiencesAfterSecondMutation);
    }

    private static WebScriptHostConfigurationContractResult RunWebScriptHostConfigurationContract(
        bool isAppService,
        bool isLinuxContainer,
        bool isLinuxAppService)
    {
        const string keyDelimiter = ":";
        string selfHostKey = ConfigurationSectionNames.WebHost
            + keyDelimiter + nameof(ScriptApplicationHostOptions.IsSelfHost);
        string scriptPathKey = ConfigurationSectionNames.WebHost
            + keyDelimiter + nameof(ScriptApplicationHostOptions.ScriptPath);
        string logPathKey = ConfigurationSectionNames.WebHost
            + keyDelimiter + nameof(ScriptApplicationHostOptions.LogPath);

        Environment.SetEnvironmentVariable(
            EnvironmentSettingNames.AzureWebsiteHomePath,
            "first-home");
        Environment.SetEnvironmentVariable(
            EnvironmentSettingNames.AzureWebJobsScriptRoot,
            "first-root");
        Environment.SetEnvironmentVariable(
            EnvironmentSettingNames.FunctionsLogPath,
            "first-logs");
        WebScriptHostConfigurationSource source = new()
        {
            IsAppServiceEnvironment = isAppService,
            IsLinuxContainerEnvironment = isLinuxContainer,
            IsLinuxAppServiceEnvironment = isLinuxAppService
        };
        IConfigurationProvider provider = source.Build(new ConfigurationBuilder());
        provider.Load();
        string firstSelfHost = GetProviderValue(provider, selfHostKey);
        string firstScriptPath = GetProviderValue(provider, scriptPathKey);
        string firstLogPath = GetProviderValue(provider, logPathKey);

        Environment.SetEnvironmentVariable(
            EnvironmentSettingNames.AzureWebsiteHomePath,
            "second-home");
        Environment.SetEnvironmentVariable(
            EnvironmentSettingNames.AzureWebJobsScriptRoot,
            "second-root");
        Environment.SetEnvironmentVariable(
            EnvironmentSettingNames.FunctionsLogPath,
            "second-logs");
        Environment.SetEnvironmentVariable(
            EnvironmentSettingNames.AzureWebsiteInstanceId,
            isAppService ? null : "new-app-service-marker");
        provider.Load();

        return new WebScriptHostConfigurationContractResult(
            firstSelfHost,
            firstScriptPath,
            firstLogPath,
            GetProviderValue(provider, selfHostKey),
            GetProviderValue(provider, scriptPathKey),
            GetProviderValue(provider, logPathKey));
    }

    private static string GetProviderValue(IConfigurationProvider provider, string key)
    {
        return provider.TryGet(key, out string value)
            ? value
            : null;
    }

    private static void InvokeMessageReceived(
        JwtBearerOptions options, IServiceProvider serviceProvider)
    {
        DefaultHttpContext httpContext = new()
        {
            RequestServices = serviceProvider
        };
        AuthenticationScheme scheme = new(
            JwtBearerDefaults.AuthenticationScheme,
            displayName: null,
            typeof(JwtBearerHandler));
        MessageReceivedContext context = new(httpContext, scheme, options);
        options.Events.MessageReceived(context).GetAwaiter().GetResult();
    }

    private static bool InvokeEnvironmentBoolean(object environment, string methodName)
    {
        Type extensionsType = typeof(SystemEnvironment).Assembly.GetType(
            "Microsoft.Azure.WebJobs.Script.EnvironmentExtensions",
            throwOnError: true);
        MethodInfo method = extensionsType.GetMethod(
            methodName,
            BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMethodException(extensionsType.FullName, methodName);
        return (bool)method.Invoke(null, [environment]);
    }

    private static OSPlatform ParsePlatform(string platform)
    {
        if (string.Equals(platform, OSPlatform.Windows.ToString(), StringComparison.Ordinal))
        {
            return OSPlatform.Windows;
        }

        if (string.Equals(platform, OSPlatform.Linux.ToString(), StringComparison.Ordinal))
        {
            return OSPlatform.Linux;
        }

        throw new InvalidOperationException($"Unsupported platform '{platform}'.");
    }

    private static string FormatMethodSignature(MethodInfo method)
    {
        string parameters = string.Join(
            ", ",
            method.GetParameters().Select((parameter, index) =>
            {
                string modifier = parameter.IsOut
                    ? "out "
                    : parameter.ParameterType.IsByRef
                        ? "ref "
                        : method.IsDefined(typeof(ExtensionAttribute), inherit: false)
                            && index == 0
                                ? "this "
                                : string.Empty;
                string defaultValue = parameter.HasDefaultValue
                    ? $" = {FormatDefaultValue(parameter.DefaultValue)}"
                    : string.Empty;
                return $"{modifier}{FormatTypeName(parameter.ParameterType)} {parameter.Name}{defaultValue}";
            }));

        return $"method {FormatTypeName(method.ReturnType)} {FormatTypeName(method.DeclaringType)}.{method.Name}({parameters})";
    }

    private static string FormatDefaultValue(object value)
    {
        return value switch
        {
            null => "null",
            string text => $"\"{text}\"",
            char character => $"'{character}'",
            bool boolean => boolean ? "true" : "false",
            Missing => "missing",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        };
    }

    private static string FormatTypeName(Type type)
    {
        if (type.IsByRef)
        {
            return FormatTypeName(type.GetElementType());
        }

        if (type.IsArray)
        {
            return $"{FormatTypeName(type.GetElementType())}[{new string(',', type.GetArrayRank() - 1)}]";
        }

        if (!type.IsGenericType)
        {
            return (type.FullName ?? type.Name).Replace('+', '.');
        }

        string genericName = type.GetGenericTypeDefinition().FullName;
        genericName = genericName[..genericName.IndexOf('`')].Replace('+', '.');
        return $"{genericName}<{string.Join(", ", type.GetGenericArguments().Select(FormatTypeName))}>";
    }

    private static void WriteResult(object result)
    {
        Console.WriteLine(
            EnvironmentVariablesConfigurationTestContracts.ResultPrefix
            + JsonSerializer.Serialize(
                result,
                result.GetType(),
                EnvironmentBehaviorParityTestContracts.SerializerOptions));
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

    private static SpecializationMutationContractResult RunSpecializationMutationContract()
    {
        string key = $"AFHOSTSPECIALIZATION{Guid.NewGuid():N}".ToUpperInvariant();
        const string assignedValue = "assigned-value";
        IConfigurationRoot live = BuildConfiguration(liveEnvironmentLoading: true);
        IConfigurationRoot cached = BuildConfiguration(liveEnvironmentLoading: false);
        using Process childStartedBeforeMutation = StartChildProcessRead(key);

        try
        {
            Environment.SetEnvironmentVariable(key, assignedValue);
            LookupResult literalBeforeReload = ReadLiteral(key);
            LookupResult liveBeforeReload = ReadConfiguration(live, key);
            LookupResult cachedBeforeReload = ReadConfiguration(cached, key);
            LookupResult liveEnumerationBeforeReload = FindEntry(live, key);
            LookupResult cachedEnumerationBeforeReload = FindEntry(cached, key);
            LookupResult beforeMutationChildResult = CompleteChildProcessRead(
                childStartedBeforeMutation,
                key);
            using Process childStartedAfterMutation = StartChildProcessRead(key);
            LookupResult afterMutationChildResult = CompleteChildProcessRead(
                childStartedAfterMutation,
                key);

            live.Reload();

            return new SpecializationMutationContractResult(
                literalBeforeReload,
                liveBeforeReload,
                cachedBeforeReload,
                liveEnumerationBeforeReload,
                cachedEnumerationBeforeReload,
                beforeMutationChildResult,
                afterMutationChildResult,
                ReadConfiguration(live, key),
                ReadConfiguration(cached, key),
                FindEntry(live, key),
                FindEntry(cached, key));
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, null);
            if (!childStartedBeforeMutation.HasExited)
            {
                childStartedBeforeMutation.Kill(entireProcessTree: true);
            }
        }
    }

    private static Process StartChildProcessRead(string key)
    {
        string dotnetHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (string.IsNullOrEmpty(dotnetHost))
        {
            dotnetHost = "dotnet";
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = dotnetHost,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(typeof(Program).Assembly.Location);
        startInfo.ArgumentList.Add(EnvironmentVariablesConfigurationTestContracts.ChildProcessReadScenario);
        startInfo.ArgumentList.Add(key);

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start the child-process inheritance contract.");
    }

    private static LookupResult CompleteChildProcessRead(Process process, string key)
    {
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        process.StandardInput.WriteLine();
        process.StandardInput.Close();

        if (!process.WaitForExit((int)TimeSpan.FromSeconds(30).TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new TimeoutException($"Child-process inheritance read for '{key}' exceeded 30 seconds.");
        }

        string output = standardOutput.GetAwaiter().GetResult();
        string error = standardError.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Child-process inheritance read for '{key}' exited with code {process.ExitCode}."
                + $"{Environment.NewLine}{error}{Environment.NewLine}{output}");
        }

        string resultLine = output
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .SingleOrDefault(line => line.StartsWith(
                EnvironmentVariablesConfigurationTestContracts.ResultPrefix,
                StringComparison.Ordinal));
        if (string.IsNullOrEmpty(resultLine))
        {
            throw new InvalidOperationException(
                $"Child-process inheritance read for '{key}' did not emit a result."
                + $"{Environment.NewLine}{error}{Environment.NewLine}{output}");
        }

        return JsonSerializer.Deserialize<LookupResult>(
            resultLine[EnvironmentVariablesConfigurationTestContracts.ResultPrefix.Length..])
            ?? throw new InvalidOperationException(
                $"Unable to deserialize child-process inheritance read for '{key}'.");
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
