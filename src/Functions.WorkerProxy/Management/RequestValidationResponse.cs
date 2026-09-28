// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Azure.Functions.WorkerProxy.Management;

/// <summary>
/// Contains detected field errors, or one request-body error, returned with HTTP 400.
/// </summary>
internal sealed record RequestValidationResponse(IReadOnlyList<RequestValidationError> Errors);
