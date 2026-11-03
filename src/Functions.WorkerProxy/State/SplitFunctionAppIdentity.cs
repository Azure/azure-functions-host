// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json.Serialization;

namespace Azure.Functions.WorkerProxy.State;

internal sealed record SplitFunctionAppIdentity(
    [property: JsonRequired] string FunctionAppName,
    [property: JsonRequired] int FunctionAppSiteId,
    [property: JsonRequired] string AntaresStampName);
