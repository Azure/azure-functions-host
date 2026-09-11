// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Azure.Functions.WorkerProxy.Management;

/// <summary>
/// Wraps validation and lifecycle failures in the same management API error envelope.
/// </summary>
internal sealed record WorkerApiErrorResponse(WorkerApiError Error);
