// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Azure.Functions.WorkerProxy;

/// <summary>
/// Defines the listener ports for the FunctionRpc relay.
/// </summary>
internal sealed class FunctionRpcRelayOptions
{
    /// <summary>
    /// The configuration key for the runtime-facing gRPC listener port.
    /// </summary>
    internal const string RuntimeGrpcPortKey = "runtime-grpc-port";

    /// <summary>
    /// The configuration key for the worker-facing gRPC listener port.
    /// </summary>
    internal const string WorkerGrpcPortKey = "worker-grpc-port";

    /// <summary>
    /// The default runtime-facing gRPC listener port.
    /// </summary>
    internal const int DefaultRuntimeGrpcPort = 50053;

    /// <summary>
    /// The default worker-facing gRPC listener port.
    /// </summary>
    internal const int DefaultWorkerGrpcPort = 50054;

    private const int MaximumPort = 65535;

    private FunctionRpcRelayOptions(int runtimeGrpcPort, int workerGrpcPort)
    {
        RuntimeGrpcPort = runtimeGrpcPort;
        WorkerGrpcPort = workerGrpcPort;
    }

    /// <summary>
    /// Gets the runtime-facing gRPC listener port.
    /// </summary>
    public int RuntimeGrpcPort { get; }

    /// <summary>
    /// Gets the worker-facing gRPC listener port.
    /// </summary>
    public int WorkerGrpcPort { get; }

    /// <summary>
    /// Creates validated relay options from command-line or environment configuration.
    /// </summary>
    /// <param name="configuration">The WorkerProxy configuration.</param>
    /// <returns>The validated relay options.</returns>
    /// <exception cref="InvalidOperationException">
    /// A configured value is outside its supported range or both fixed listener ports are equal.
    /// </exception>
    public static FunctionRpcRelayOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        int runtimeGrpcPort = ReadInt32(configuration, RuntimeGrpcPortKey, DefaultRuntimeGrpcPort, minimumValue: 0, MaximumPort);
        int workerGrpcPort = ReadInt32(configuration, WorkerGrpcPortKey, DefaultWorkerGrpcPort, minimumValue: 0, MaximumPort);

        // Port zero asks Kestrel to select an ephemeral port and is used by in-process tests.
        if (runtimeGrpcPort != 0 && runtimeGrpcPort == workerGrpcPort)
        {
            throw new InvalidOperationException($"Configuration values '{RuntimeGrpcPortKey}' and " +
                $"'{WorkerGrpcPortKey}' must identify different ports.");
        }

        return new FunctionRpcRelayOptions(runtimeGrpcPort, workerGrpcPort);
    }

    private static int ReadInt32(IConfiguration configuration, string key, int defaultValue, int minimumValue, int maximumValue)
    {
        string environmentKey = key.Replace('-', '_').ToUpperInvariant();
        string? configuredValue = configuration[key] ?? configuration[environmentKey];
        if (configuredValue is null)
        {
            return defaultValue;
        }

        if (!int.TryParse(configuredValue, NumberStyles.None, CultureInfo.InvariantCulture, out int value)
            || value < minimumValue
            || value > maximumValue)
        {
            throw new InvalidOperationException($"Configuration value '{key}' must be an integer between {minimumValue} and {maximumValue}.");
        }

        return value;
    }
}
