// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Grpc.HttpProxy;

namespace Azure.Functions.WorkerProxy.Identity;

internal sealed class IdentityProxyOptions : HttpProxyOptions
{
    public const string SectionName = "WorkerProxy:IdentityProxy";

    public const string SecretSettingName = "IDENTITY_HEADER";

    public int Port { get; set; } = 8081;

    public string TokenPath { get; set; } = "/msi/token";
}
