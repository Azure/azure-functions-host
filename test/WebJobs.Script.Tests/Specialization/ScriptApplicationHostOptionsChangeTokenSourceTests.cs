// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.Standby;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Specialization;

public class ScriptApplicationHostOptionsChangeTokenSourceTests : EnvironmentContractTestBase
{
    [Fact]
    public void StandbyRefreshPrecedesScriptApplicationRefreshWhilePlainOptionsRemainCached()
    {
        ContractState state = new();
        TestChangeTokenSource<StandbyOptions> standbyToken = new();
        ServiceCollection services = new();
        services.AddOptions();
        services.AddSingleton(state);
        services.AddSingleton<IConfigureOptions<StandbyOptions>, RecordingStandbyOptionsSetup>();
        services.AddSingleton<IOptionsChangeTokenSource<StandbyOptions>>(standbyToken);
        services.AddSingleton<IConfigureOptions<ScriptApplicationHostOptions>, RecordingScriptApplicationHostOptionsSetup>();
        services.AddSingleton<
            IOptionsChangeTokenSource<ScriptApplicationHostOptions>,
            ScriptApplicationHostOptionsChangeTokenSource>();

        using ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<StandbyOptions> standbyMonitor =
            provider.GetRequiredService<IOptionsMonitor<StandbyOptions>>();
        IOptionsMonitor<ScriptApplicationHostOptions> scriptMonitor =
            provider.GetRequiredService<IOptionsMonitor<ScriptApplicationHostOptions>>();
        IOptions<ScriptApplicationHostOptions> scriptOptions =
            provider.GetRequiredService<IOptions<ScriptApplicationHostOptions>>();

        Assert.True(standbyMonitor.CurrentValue.InStandbyMode);
        Assert.True(scriptMonitor.CurrentValue.IsStandbyConfiguration);
        Assert.True(scriptOptions.Value.IsStandbyConfiguration);
        state.Events.Clear();
        state.InStandbyMode = false;

        standbyToken.SignalChange();

        Assert.Equal(
            ["standby:False", "script-application:False"],
            state.Events);
        Assert.False(standbyMonitor.CurrentValue.InStandbyMode);
        Assert.False(scriptMonitor.CurrentValue.IsStandbyConfiguration);
        Assert.True(scriptOptions.Value.IsStandbyConfiguration);
    }

    [Fact]
    public void StandbyManagerSignalUsesProductionStandbyTokenSourceBeforeScriptApplicationRefresh()
    {
        StandbyManager.ResetChangeToken();

        try
        {
            ContractState state = new();
            ServiceCollection services = new();
            services.AddOptions();
            services.AddSingleton(state);
            services.AddSingleton<IConfigureOptions<StandbyOptions>, RecordingStandbyOptionsSetup>();
            services.AddSingleton<IOptionsChangeTokenSource<StandbyOptions>, StandbyChangeTokenSource>();
            services.AddSingleton<IConfigureOptions<ScriptApplicationHostOptions>, RecordingScriptApplicationHostOptionsSetup>();
            services.AddSingleton<
                IOptionsChangeTokenSource<ScriptApplicationHostOptions>,
                ScriptApplicationHostOptionsChangeTokenSource>();

            using ServiceProvider provider = services.BuildServiceProvider();
            IOptionsMonitor<StandbyOptions> standbyMonitor =
                provider.GetRequiredService<IOptionsMonitor<StandbyOptions>>();
            IOptionsMonitor<ScriptApplicationHostOptions> scriptMonitor =
                provider.GetRequiredService<IOptionsMonitor<ScriptApplicationHostOptions>>();
            Assert.True(standbyMonitor.CurrentValue.InStandbyMode);
            Assert.True(scriptMonitor.CurrentValue.IsStandbyConfiguration);
            state.Events.Clear();
            state.InStandbyMode = false;

            using StandbyManager manager = new(
                Mock.Of<IScriptHostManager>(),
                Mock.Of<IWebHostWorkerManager>(),
                Mock.Of<IConfigurationRoot>(),
                Mock.Of<IScriptWebHostEnvironment>(),
                _testEnvironment,
                Mock.Of<IOptionsMonitor<ScriptApplicationHostOptions>>(),
                NullLogger<StandbyManager>.Instance,
                new HostNameProvider(_testEnvironment),
                Mock.Of<IHostApplicationLifetime>(),
                new TestMetricsLogger());
            manager.NotifyChange();

            Assert.Equal(
                ["standby:False", "script-application:False"],
                state.Events);
            Assert.False(standbyMonitor.CurrentValue.InStandbyMode);
            Assert.False(scriptMonitor.CurrentValue.IsStandbyConfiguration);
        }
        finally
        {
            StandbyManager.ResetChangeToken();
        }
    }

    private sealed class ContractState
    {
        public bool InStandbyMode { get; set; } = true;

        public List<string> Events { get; } = [];
    }

    private sealed class RecordingStandbyOptionsSetup : IConfigureOptions<StandbyOptions>
    {
        private readonly ContractState _state;

        public RecordingStandbyOptionsSetup(ContractState state)
        {
            _state = state;
        }

        public void Configure(StandbyOptions options)
        {
            options.InStandbyMode = _state.InStandbyMode;
            _state.Events.Add($"standby:{options.InStandbyMode}");
        }
    }

    private sealed class RecordingScriptApplicationHostOptionsSetup :
        IConfigureOptions<ScriptApplicationHostOptions>
    {
        private readonly ContractState _state;
        private readonly IOptionsMonitor<StandbyOptions> _standbyOptions;

        public RecordingScriptApplicationHostOptionsSetup(
            ContractState state,
            IOptionsMonitor<StandbyOptions> standbyOptions)
        {
            _state = state;
            _standbyOptions = standbyOptions;
        }

        public void Configure(ScriptApplicationHostOptions options)
        {
            options.IsStandbyConfiguration = _standbyOptions.CurrentValue.InStandbyMode;
            _state.Events.Add($"script-application:{options.IsStandbyConfiguration}");
        }
    }
}
