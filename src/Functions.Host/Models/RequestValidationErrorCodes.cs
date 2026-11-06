// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Azure.Functions.Host.Models;

// Stable wire codes; tests assert the serialized values.
internal static class RequestValidationErrorCodes
{
    internal const string InvalidBody = nameof(InvalidBody);
    internal const string InvalidEndpoint = nameof(InvalidEndpoint);
    internal const string InvalidValue = nameof(InvalidValue);
    internal const string Required = nameof(Required);
}
