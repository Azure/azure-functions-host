// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Text.Json.Serialization;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Identifies the app-scoped runtime initiating a worker link.
/// </summary>
/// <param name="ResourceNamespace">The Legion resource namespace.</param>
/// <param name="PodName">The runtime pod name.</param>
/// <param name="PodGuid">The runtime pod GUID.</param>
/// <param name="LegionStampName">The Legion stamp name.</param>
/// <param name="PodType">The Bala pod type.</param>
/// <param name="App">The function app identity.</param>
public sealed record RuntimePodIdentity(
    string ResourceNamespace,
    string PodName,
    Guid PodGuid,
    string LegionStampName,
    string PodType,
    RuntimeFunctionAppIdentity App)
{
    /// <summary>
    /// Gets the binary FunctionRpc metadata member carrying the runtime identity.
    /// </summary>
    public const string MetadataName = "x-functions-runtime-identity-bin";

    /// <summary>
    /// Gets the canonical Bala runtime pod type.
    /// </summary>
    public const string RuntimePodType = "FunctionsSplitRuntimePod";
}

/// <summary>
/// Identifies the function app owned by a runtime pod.
/// </summary>
/// <param name="FunctionAppName">The function app name.</param>
/// <param name="FunctionAppSiteId">The Antares site ID.</param>
/// <param name="AntaresStampName">The Antares stamp name.</param>
public sealed record RuntimeFunctionAppIdentity(
    string FunctionAppName,
    [property: JsonRequired]
    [property: Newtonsoft.Json.JsonProperty(Required = Newtonsoft.Json.Required.Always)]
    int FunctionAppSiteId,
    string AntaresStampName);
