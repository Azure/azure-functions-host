// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json.Serialization;

namespace Azure.Functions.WorkerProxy.State;

internal sealed record SplitPodIdentity(
    [property: JsonRequired] string ResourceNamespace,
    [property: JsonRequired] string PodName,
    [property: JsonRequired] SplitFunctionAppIdentity App);
