// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Grpc.TokenServiceProxy;

namespace Azure.Functions.WorkerProxy.Identity;

/// <summary>
/// Configures the loopback Token Service listener separately from the Host-selected upstream endpoint and credential.
/// </summary>
internal sealed class TokenServiceProxyWorkerOptions : TokenServiceProxyOptions
{
    public const string SectionName = "WorkerProxy:TokenServiceProxy";

    public const string SecretSettingName = "IDENTITY_HEADER";

    public int Port { get; set; } = 8081;

    public string TokenPath { get; set; } = "/msi/token";
}
