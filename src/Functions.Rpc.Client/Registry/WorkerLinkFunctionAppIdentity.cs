// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Identifies the function app associated with a Runtime pod.
/// </summary>
/// <param name="FunctionAppName">The function app name.</param>
/// <param name="FunctionAppSiteId">The platform site identifier.</param>
/// <param name="AntaresStampName">The Antares stamp hosting the function app.</param>
public sealed record WorkerLinkFunctionAppIdentity(string? FunctionAppName, int? FunctionAppSiteId, string? AntaresStampName);
