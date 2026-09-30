// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Moq;

namespace Azure.Functions.Host.Tests.WorkerLink;

internal static class TestScriptApplicationHostOptions
{
    // IsStandbyConfiguration has an internal setter, so standby options come from the production setup.
    internal static ScriptApplicationHostOptions CreateStandby()
    {
        Mock<IOptionsMonitor<StandbyOptions>> standbyOptions = new();
        standbyOptions.SetupGet(options => options.CurrentValue).Returns(new StandbyOptions { InStandbyMode = true });
        ScriptApplicationHostOptionsSetup setup = new(new ConfigurationBuilder().Build(), standbyOptions.Object,
            Mock.Of<IServiceProvider>(), Mock.Of<IEnvironment>());
        ScriptApplicationHostOptions options = new();
        setup.Configure(options);

        return options;
    }
}
