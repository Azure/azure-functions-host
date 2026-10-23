// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Azure.Functions.Authentication;
using Azure.Functions.Rpc.Client;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

[Collection(nameof(RuntimeEnvironmentCollection))]
public class WorkerRuntimeConfigurationTests
{
    [Fact]
    public void Credentials_ReadSpecializedAppSettingsThroughExistingRootConfiguration()
    {
        using EnvironmentVariableScope initialName = new(RuntimeWorkerAuthentication.AppNameSetting, null);
        using EnvironmentVariableScope initialKey = new(RuntimeWorkerAuthentication.AppKeySetting, null);
        IConfiguration configuration = new ConfigurationBuilder().Add(new ScriptEnvironmentVariablesConfigurationSource()).Build();
        RuntimeWorkerCredentials credentials = new("worker-pod", RuntimeWorkerTestData.WorkerKey, configuration, TimeProvider.System);

        Assert.Single(credentials.CreateHeaders());
        WorkerLinkException missing = Assert.Throws<WorkerLinkException>(() => credentials.ValidateAppProof("missing-proof"));
        Assert.Equal(WorkerLinkFailureReason.AuthenticationUnavailable, missing.Reason);

        byte[] appKey = SHA256.HashData("specialized-test-app-key"u8);
        using EnvironmentVariableScope specializedName = new(RuntimeWorkerAuthentication.AppNameSetting, "specialized-app");
        using EnvironmentVariableScope specializedKey = new(RuntimeWorkerAuthentication.AppKeySetting, Convert.ToHexString(appKey));
        IReadOnlyDictionary<string, string> headers = credentials.CreateHeaders();

        Assert.True(RuntimeWorkerAuthentication.ValidateToken(headers[RuntimeWorkerAuthentication.WorkerTokenHeader],
            Convert.FromBase64String(RuntimeWorkerTestData.WorkerKey),
            RuntimeWorkerAuthentication.WorkerAudience("worker-pod"), RuntimeWorkerAuthentication.WorkerPurpose, DateTimeOffset.UtcNow));
        credentials.ValidateAppProof(RuntimeWorkerAuthentication.CreateAppProof(appKey, "specialized-app", "worker-pod", DateTimeOffset.UtcNow));
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previous;

        public EnvironmentVariableScope(string name, string? value)
        {
            _name = name;
            _previous = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose() => Environment.SetEnvironmentVariable(_name, _previous);
    }
}

[CollectionDefinition(nameof(RuntimeEnvironmentCollection), DisableParallelization = true)]
public sealed class RuntimeEnvironmentCollection;
