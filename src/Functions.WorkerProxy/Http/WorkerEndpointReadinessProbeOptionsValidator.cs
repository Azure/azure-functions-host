// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Options;

namespace Azure.Functions.WorkerProxy.Http;

/// <summary>
/// Validates <see cref="WorkerEndpointReadinessProbeOptions"/>.
/// </summary>
[OptionsValidator]
internal sealed partial class WorkerEndpointReadinessProbeOptionsValidator
    : IValidateOptions<WorkerEndpointReadinessProbeOptions>
{
}
