// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Extends exception types with cleanup aggregation helpers.
/// </summary>
internal static class ExceptionExtensions
{
    extension(AggregateException)
    {
        internal static Exception Combine(Exception currentException, Exception nextException)
            => currentException is null ? nextException : new AggregateException(currentException, nextException);
    }
}
