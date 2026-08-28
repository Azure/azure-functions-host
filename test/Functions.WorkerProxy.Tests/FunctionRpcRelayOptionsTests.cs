// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public class FunctionRpcRelayOptionsTests
{
    [Fact]
    public void FromConfiguration_UsesStableDefaults()
    {
        IConfiguration configuration = CreateConfiguration(new Dictionary<string, string?>());

        FunctionRpcRelayOptions options = FunctionRpcRelayOptions.FromConfiguration(configuration);

        Assert.Equal(FunctionRpcRelayOptions.DefaultRuntimeGrpcPort, options.RuntimeGrpcPort);
        Assert.Equal(FunctionRpcRelayOptions.DefaultWorkerGrpcPort, options.WorkerGrpcPort);
    }

    [Fact]
    public void FromConfiguration_ReadsEnvironmentStyleKeys()
    {
        IConfiguration configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["RUNTIME_GRPC_PORT"] = "41001",
            ["WORKER_GRPC_PORT"] = "41002"
        });

        FunctionRpcRelayOptions options = FunctionRpcRelayOptions.FromConfiguration(configuration);

        Assert.Equal(41001, options.RuntimeGrpcPort);
        Assert.Equal(41002, options.WorkerGrpcPort);
    }

    [Theory]
    [InlineData(FunctionRpcRelayOptions.RuntimeGrpcPortKey, "-1")]
    [InlineData(FunctionRpcRelayOptions.RuntimeGrpcPortKey, "not-a-number")]
    [InlineData(FunctionRpcRelayOptions.WorkerGrpcPortKey, "65536")]
    public void FromConfiguration_RejectsInvalidValues(string key, string value)
    {
        IConfiguration configuration = CreateConfiguration(new Dictionary<string, string?> { [key] = value });

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() => FunctionRpcRelayOptions.FromConfiguration(configuration));

        Assert.Contains(key, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromConfiguration_RejectsDuplicateFixedPorts()
    {
        IConfiguration configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            [FunctionRpcRelayOptions.RuntimeGrpcPortKey] = "41001",
            [FunctionRpcRelayOptions.WorkerGrpcPortKey] = "41001"
        });

        Assert.Throws<InvalidOperationException>(() => FunctionRpcRelayOptions.FromConfiguration(configuration));
    }

    private static IConfiguration CreateConfiguration(IReadOnlyDictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
