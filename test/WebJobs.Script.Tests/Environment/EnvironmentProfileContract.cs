// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.WebJobs.Script.Tests;
using static Microsoft.Azure.WebJobs.Script.EnvironmentSettingNames;

namespace Microsoft.Azure.WebJobs.Script.Config.Tests;

internal enum HostingEnvironmentProfile
{
    LocalSelfHost,
    CoreTools,
    WindowsDedicated,
    WindowsConsumption,
    WindowsElasticPremium,
    LinuxAppService,
    LinuxConsumptionAtlas,
    LinuxConsumptionLegion,
    FlexConsumptionLegion,
    ContainerApps,
    Kubernetes
}

internal enum MarkerEvidence
{
    CompleteCurrentInputs,
    Incomplete,
    ContradictoryOrOverlapping
}

internal enum HostPhase
{
    NonPlaceholderStartup,
    PlaceholderBeforeAssignment,
    AssignmentAppliedBeforeReload,
    AfterReloadBeforeStandbyToken,
    AfterStandbyToken,
    AfterWorkerSpecialization,
    AfterScriptHostChildRebuild
}

internal sealed record EnvironmentProfileContract(
    HostingEnvironmentProfile Profile,
    string DefaultPlatform,
    Dictionary<string, string> Markers);

internal sealed record EnvironmentHelperResult(string Signature, string Value);

internal sealed record EnvironmentProfileResult(
    HostingEnvironmentProfile Profile,
    string Platform,
    bool Is64BitProcess,
    EnvironmentHelperResult[] Helpers);

internal sealed record EnvironmentStableFactVariantContract(
    string Name,
    HostingEnvironmentProfile Profile,
    TestProcessFacts ProcessFacts,
    Dictionary<string, string> VariableOverrides);

internal sealed record EnvironmentStableFactVariantResult(
    string Name,
    HostingEnvironmentProfile Profile,
    string Platform,
    bool Is64BitProcess,
    EnvironmentHelperResult[] Helpers);

internal sealed record EnvironmentHelperMatrixResult(
    EnvironmentProfileResult[] Profiles,
    EnvironmentStableFactVariantResult[] StableFactVariants,
    EnvironmentMarkerObservationResult[] Observations);

internal sealed record EnvironmentMarkerObservationContract(
    string Name,
    HostingEnvironmentProfile? Profile,
    MarkerEvidence Evidence,
    HostPhase Phase,
    string Platform,
    Dictionary<string, string> Markers);

internal sealed record EnvironmentMarkerObservationResult(
    string Name,
    HostingEnvironmentProfile? Profile,
    MarkerEvidence Evidence,
    HostPhase Phase,
    Dictionary<string, string> Predicates);

internal sealed record StaticCacheContractResult(
    bool PlaceholderApplicationInsights,
    bool SpecializedApplicationInsights,
    bool MutatedApplicationInsights,
    bool InitialMultiLanguage,
    bool MutatedMultiLanguage);

internal sealed record JwtLatchContractResult(
    string[] PlaceholderAudiences,
    string[] SpecializedAudiences,
    string[] AudiencesAfterSecondMutation);

internal sealed record WebScriptHostConfigurationContractResult(
    string FirstSelfHost,
    string FirstScriptPath,
    string FirstLogPath,
    string SecondSelfHost,
    string SecondScriptPath,
    string SecondLogPath);

internal static class EnvironmentBehaviorParityTestContracts
{
    public const string HelperMatrixScenario = "environment-helper-matrix";
    public const string StaticCacheScenario = "environment-static-cache";
    public const string JwtLatchScenario = "jwt-specialization-latch";
    public const string WebScriptHostConfigurationScenario = "web-script-host-configuration";

    public static JsonSerializerOptions SerializerOptions { get; } = CreateSerializerOptions();

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        JsonSerializerOptions options = new();
        options.Converters.Add(new JsonStringEnumConverter());

        return options;
    }
}

internal static class EnvironmentBehaviorParityFixtures
{
    private static readonly HostPhase[] AllPhases = Enum.GetValues<HostPhase>();

    public static EnvironmentProfileContract[] CompleteProfiles { get; } =
    [
        Profile(HostingEnvironmentProfile.LocalSelfHost, OSPlatform.Windows),
        Profile(HostingEnvironmentProfile.CoreTools, OSPlatform.Windows,
            (CoreToolsEnvironment, "1")),
        Profile(HostingEnvironmentProfile.WindowsDedicated, OSPlatform.Windows,
            (AzureWebsiteInstanceId, "dedicated-instance")),
        Profile(HostingEnvironmentProfile.WindowsConsumption, OSPlatform.Windows,
            (AzureWebsiteInstanceId, "consumption-instance"),
            (AzureWebsiteSku, ScriptConstants.DynamicSku)),
        Profile(HostingEnvironmentProfile.WindowsElasticPremium, OSPlatform.Windows,
            (AzureWebsiteInstanceId, "premium-instance"),
            (AzureWebsiteSku, ScriptConstants.ElasticPremiumSku)),
        Profile(HostingEnvironmentProfile.LinuxAppService, OSPlatform.Linux,
            (AzureWebsiteInstanceId, "linux-app-service-instance"),
            (FunctionsLogsMountPath, "/home/LogFiles")),
        Profile(HostingEnvironmentProfile.LinuxConsumptionAtlas, OSPlatform.Linux,
            (ContainerName, "atlas-container")),
        Profile(HostingEnvironmentProfile.LinuxConsumptionLegion, OSPlatform.Linux,
            (ContainerName, "legion-container"),
            (LegionServiceHost, "legion.internal"),
            (AzureWebsiteSku, ScriptConstants.DynamicSku)),
        Profile(HostingEnvironmentProfile.FlexConsumptionLegion, OSPlatform.Linux,
            (WebsitePodName, "flex-pod"),
            (ContainerName, "flex-container"),
            (LegionServiceHost, "legion.internal"),
            (AzureWebsiteSku, ScriptConstants.FlexConsumptionSku)),
        Profile(HostingEnvironmentProfile.ContainerApps, OSPlatform.Linux,
            (ManagedEnvironment, "true")),
        Profile(HostingEnvironmentProfile.Kubernetes, OSPlatform.Linux,
            (KubernetesServiceHost, "10.0.0.1"),
            (KubernetesServiceHttpsPort, "443"),
            (PodNamespace, "functions"))
    ];

    public static EnvironmentStableFactVariantContract[] StableFactVariants { get; } =
    [
        StableFactVariant(
            "LocalSelfHost:Linux64Bit",
            HostingEnvironmentProfile.LocalSelfHost,
            OSPlatform.Linux,
            is64BitProcess: true),
        StableFactVariant(
            "CoreTools:Linux64Bit",
            HostingEnvironmentProfile.CoreTools,
            OSPlatform.Linux,
            is64BitProcess: true),
        StableFactVariant(
            "LocalSelfHost:Windows32Bit",
            HostingEnvironmentProfile.LocalSelfHost,
            OSPlatform.Windows,
            is64BitProcess: false),
        StableFactVariant(
            "WindowsConsumption:WindowsNonVmss",
            HostingEnvironmentProfile.WindowsConsumption,
            OSPlatform.Windows,
            is64BitProcess: true,
            (RoleInstanceId, null))
    ];

    public static EnvironmentMarkerObservationContract[] MarkerObservations { get; } =
        CreateMarkerObservations();

    public static bool IsPlaceholderPhase(HostPhase phase)
    {
        return phase is HostPhase.PlaceholderBeforeAssignment
            or HostPhase.AssignmentAppliedBeforeReload
            or HostPhase.AfterReloadBeforeStandbyToken;
    }

    public static Dictionary<string, string> CreateVariables(EnvironmentProfileContract profile)
    {
        Dictionary<string, string> variables = new(StringComparer.Ordinal)
        {
            [AzureWebsitePlaceholderMode] = "0",
            [AzureWebsiteName] = "Parity-App--",
            [AzureWebsiteSlotName] = "Staging",
            [AzureWebsiteRuntimeSiteName] = "Parity-App__Runtime",
            [AzureWebsiteHostName] = "Parity-App.AzureWebsites.Net.azurewebsites.net",
            [AzureWebsiteOwnerName] = "subscription-id+resource-group",
            [AzureWebsiteHomePath] = "parity-home",
            [AzureWebsiteContainerReady] = "ready",
            [AzureWebsiteInstanceId] = profile.Markers.GetValueOrDefault(AzureWebsiteInstanceId),
            [AzureWebsiteSku] = profile.Markers.GetValueOrDefault(AzureWebsiteSku),
            [AzureWebsiteSkuName] = profile.Markers.GetValueOrDefault(AzureWebsiteSkuName),
            [ContainerName] = profile.Markers.GetValueOrDefault(ContainerName),
            [WebsitePodName] = profile.Markers.GetValueOrDefault(WebsitePodName),
            [LegionServiceHost] = profile.Markers.GetValueOrDefault(LegionServiceHost),
            [ManagedEnvironment] = profile.Markers.GetValueOrDefault(ManagedEnvironment),
            [KubernetesServiceHost] = profile.Markers.GetValueOrDefault(KubernetesServiceHost),
            [KubernetesServiceHttpsPort] = profile.Markers.GetValueOrDefault(KubernetesServiceHttpsPort),
            [PodNamespace] = profile.Markers.GetValueOrDefault(PodNamespace),
            [CoreToolsEnvironment] = profile.Markers.GetValueOrDefault(CoreToolsEnvironment),
            [FunctionsLogsMountPath] = profile.Markers.GetValueOrDefault(FunctionsLogsMountPath),
            [RunningInContainer] = "TrUe",
            [FunctionsRuntimeScaleMonitoringEnabled] = "1",
            [FunctionsAdminIsolationEnabled] = "1",
            [EasyAuthEnabled] = "TrUe",
            [AzureMonitorCategories] = "FunctionAppLogs",
            [RemoteDebuggingPort] = "4024",
            [AzureWebsiteRunFromPackage] = "1",
            [AzureFilesConnectionString] = "UseDevelopmentStorage=true",
            [AzureFilesContentShare] = "parity-share",
            [FunctionsV2CompatibilityModeKey] = "TrUe",
            [FunctionsExtensionVersion] = "~3",
            [EnableCorsConfiguration] = "1",
            [LinuxAzureAppServiceStorage] = "TrUe",
            [RoleInstanceId] = "dw0SmallDedicatedWebWorkerRole_hr0HostRole-0-VM-1",
            [FunctionsNotifyPlatformOnSync] = "TrUe",
            [AppKind] = "workflowApp",
            [AntaresPlatformVersionLinux] = "linux-version",
            [AntaresPlatformVersionWindows] = "windows-version",
            [AntaresComputerName] = "PARITY-COMPUTER",
            [FunctionsHostingEnvironmentConfigFilePath] = "hosting.json",
            [MountEnabled] = "1",
            [MeshInitURI] = "https://mesh.internal",
            [EnvironmentSettingNames.CloudName] = nameof(Microsoft.Azure.WebJobs.Script.CloudName.Fairfax),
            [FunctionsSiteUpdateId] = "site-update",
            [HttpLeaderEndpoint] = "https://leader.internal",
            [DrainOnApplicationStopping] = "false",
            [FunctionWorkerRuntime] = "node",
            [AntaresPlatformReleaseChannel] = "STANDARD",
            [AppInsightsAgent] = "TrUe",
            [TargetBaseScalingEnabled] = "0",
            [FunctionsTargetGroup] = "Function:Validation",
        };

        return variables;
    }

    private static EnvironmentProfileContract Profile(
        HostingEnvironmentProfile profile,
        OSPlatform platform,
        params (string Name, string Value)[] markers)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        foreach ((string name, string value) in markers)
        {
            values[name] = value;
        }

        return new EnvironmentProfileContract(profile, platform.ToString(), values);
    }

    private static EnvironmentStableFactVariantContract StableFactVariant(
        string name,
        HostingEnvironmentProfile profile,
        OSPlatform platform,
        bool is64BitProcess,
        params (string Name, string Value)[] variableOverrides)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        foreach ((string variableName, string variableValue) in variableOverrides)
        {
            values[variableName] = variableValue;
        }

        return new EnvironmentStableFactVariantContract(
            name,
            profile,
            new TestProcessFacts(
                platform,
                RuntimeInformation.OSArchitecture,
                is64BitProcess,
                Environment.ProcessorCount),
            values);
    }

    private static EnvironmentMarkerObservationContract[] CreateMarkerObservations()
    {
        List<EnvironmentMarkerObservationContract> observations = CompleteProfiles
            .SelectMany(profile => AllPhases.Select(phase =>
            {
                Dictionary<string, string> markers = new(
                    profile.Markers,
                    StringComparer.Ordinal)
                {
                    [AzureWebsitePlaceholderMode] =
                        IsPlaceholderPhase(phase) ? "1" : "0"
                };
                return new EnvironmentMarkerObservationContract(
                    $"{profile.Profile}:{phase}:complete-current-inputs",
                    profile.Profile,
                    MarkerEvidence.CompleteCurrentInputs,
                    phase,
                    profile.DefaultPlatform,
                    markers);
            }))
            .ToList();

        observations.AddRange(
        [
            Observation(
                "incomplete:no-markers",
                MarkerEvidence.Incomplete,
                HostPhase.PlaceholderBeforeAssignment,
                OSPlatform.Linux),
            Observation(
                "incomplete:dynamic-sku-only",
                MarkerEvidence.Incomplete,
                HostPhase.PlaceholderBeforeAssignment,
                OSPlatform.Linux,
                (AzureWebsiteSku, ScriptConstants.DynamicSku)),
            Observation(
                "incomplete:container-only",
                MarkerEvidence.Incomplete,
                HostPhase.PlaceholderBeforeAssignment,
                OSPlatform.Linux,
                (ContainerName, "provisional-container")),
            Observation(
                "incomplete:legion-only",
                MarkerEvidence.Incomplete,
                HostPhase.PlaceholderBeforeAssignment,
                OSPlatform.Linux,
                (LegionServiceHost, "legion.internal")),
            Observation(
                "incomplete:app-service-only",
                MarkerEvidence.Incomplete,
                HostPhase.PlaceholderBeforeAssignment,
                OSPlatform.Windows,
                (AzureWebsiteInstanceId, "provisional-instance")),
            Observation(
                "contradictory:app-service-and-atlas",
                MarkerEvidence.ContradictoryOrOverlapping,
                HostPhase.PlaceholderBeforeAssignment,
                OSPlatform.Linux,
                (AzureWebsiteInstanceId, "instance"),
                (ContainerName, "atlas-container")),
            Observation(
                "contradictory:atlas-and-legion",
                MarkerEvidence.ContradictoryOrOverlapping,
                HostPhase.PlaceholderBeforeAssignment,
                OSPlatform.Linux,
                (ContainerName, "container"),
                (LegionServiceHost, "legion.internal")),
            Observation(
                "contradictory:flex-and-dynamic-legion",
                MarkerEvidence.ContradictoryOrOverlapping,
                HostPhase.PlaceholderBeforeAssignment,
                OSPlatform.Linux,
                (ContainerName, "container"),
                (LegionServiceHost, "legion.internal"),
                (AzureWebsiteSku, ScriptConstants.FlexConsumptionSku),
                (AzureWebsiteSkuName, ScriptConstants.DynamicSku)),
            Observation(
                "contradictory:container-apps-and-kubernetes",
                MarkerEvidence.ContradictoryOrOverlapping,
                HostPhase.NonPlaceholderStartup,
                OSPlatform.Linux,
                (ManagedEnvironment, "true"),
                (KubernetesServiceHost, "10.0.0.1"),
                (PodNamespace, "functions")),
            Observation(
                "contradictory:core-tools-and-hosted",
                MarkerEvidence.ContradictoryOrOverlapping,
                HostPhase.NonPlaceholderStartup,
                OSPlatform.Windows,
                (CoreToolsEnvironment, "1"),
                (AzureWebsiteInstanceId, "instance"),
                (AzureWebsiteSku, ScriptConstants.DynamicSku))
        ]);

        return observations.ToArray();
    }

    private static EnvironmentMarkerObservationContract Observation(
        string name,
        MarkerEvidence evidence,
        HostPhase phase,
        OSPlatform platform,
        params (string Name, string Value)[] markers)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        foreach ((string markerName, string markerValue) in markers)
        {
            values[markerName] = markerValue;
        }

        return new EnvironmentMarkerObservationContract(
            name,
            Profile: null,
            evidence,
            phase,
            platform.ToString(),
            values);
    }
}
