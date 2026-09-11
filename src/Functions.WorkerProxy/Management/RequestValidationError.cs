// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Azure.Functions.WorkerProxy.Management;

/// <summary>
/// Identifies an invalid request field, or request for a body-level error, without echoing its value.
/// </summary>
internal sealed record RequestValidationError(string Code, string Target);
