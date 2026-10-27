// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Text.Json.Serialization;

namespace Azure.Functions.WorkerProxy.State;

internal sealed record SplitPodIdentity(
    [property: JsonRequired] string ResourceNamespace,
    [property: JsonRequired] string PodName,
    [property: JsonRequired] Guid PodGuid,
    [property: JsonRequired] string LegionStampName,
    [property: JsonRequired] string PodType,
    [property: JsonRequired] SplitFunctionAppIdentity App);
