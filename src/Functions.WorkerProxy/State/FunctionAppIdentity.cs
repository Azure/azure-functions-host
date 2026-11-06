// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json.Serialization;

namespace Azure.Functions.WorkerProxy.State;

/// <summary>
/// Identifies a function app in the platform's pod identity contract.
/// </summary>
/// <param name="FunctionAppName">The function app name.</param>
/// <param name="FunctionAppSiteId">The platform site identifier.</param>
/// <param name="AntaresStampName">The stamp hosting the function app.</param>
internal sealed record FunctionAppIdentity(
    [property: JsonRequired] string FunctionAppName,
    [property: JsonRequired] int FunctionAppSiteId,
    [property: JsonRequired] string AntaresStampName);
