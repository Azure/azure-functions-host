// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.WebHostEndToEnd;

/// <summary>Hosts a test extension whose startup explicitly registers its function provider.</summary>
public sealed class PrecompiledExtensionEndToEndTestFixture : EndToEndTestFixture
{
    private readonly IDisposable _environment;

    /// <summary>Creates a fixture without selecting the retired customer in-process runtime.</summary>
    public PrecompiledExtensionEndToEndTestFixture(string projectName, IDictionary<string, string> environment)
        : base($@"..\..\{projectName}\{TestHelpers.BuildConfig}", projectName,
            RpcWorkerConstants.DotNetIsolatedLanguageWorkerName)
    {
        _environment = new TestScopedEnvironmentVariable(environment);
    }

    protected override Task CreateTestStorageEntities() => Task.CompletedTask;

    /// <inheritdoc />
    public override async Task DisposeAsync()
    {
        try
        {
            await base.DisposeAsync();
        }
        finally
        {
            _environment.Dispose();
        }
    }
}
