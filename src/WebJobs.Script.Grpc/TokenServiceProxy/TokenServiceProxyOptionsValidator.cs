// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Grpc.TokenServiceProxy;

/// <summary>
/// Validates request limits, buffer capacity, and timeouts when the Token Service proxy is enabled.
/// </summary>
/// <typeparam name="TOptions">The Token Service proxy options type.</typeparam>
public sealed class TokenServiceProxyOptionsValidator<TOptions> : IValidateOptions<TOptions>
    where TOptions : TokenServiceProxyOptions
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        List<string> failures = [];
        ValidatePositive(options.MaxRequestBodyBytes, nameof(options.MaxRequestBodyBytes), failures);
        ValidatePositive(options.MaxResponseBodyBytes, nameof(options.MaxResponseBodyBytes), failures);
        ValidatePositive(options.MaxHeaderBytes, nameof(options.MaxHeaderBytes), failures);
        ValidatePositive(options.MaxHeaderCount, nameof(options.MaxHeaderCount), failures);
        ValidatePositive(options.MaxTargetBytes, nameof(options.MaxTargetBytes), failures);
        ValidatePositive(options.MaxPendingRequests, nameof(options.MaxPendingRequests), failures);
        ValidatePositive(options.MaxHostRequests, nameof(options.MaxHostRequests), failures);
        ValidateBufferCapacity(options.MaxBufferedBytes, options.ReservationBytes, nameof(options.MaxBufferedBytes), failures);
        ValidateBufferCapacity(options.MaxHostBufferedBytes, options.ReservationBytes, nameof(options.MaxHostBufferedBytes), failures);
        ValidateTimeout(options.RequestTimeout, nameof(options.RequestTimeout), failures);
        ValidateTimeout(options.LinkTimeout, nameof(options.LinkTimeout), failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidatePositive(int value, string propertyName, List<string> failures)
    {
        if (value <= 0)
        {
            failures.Add($"{propertyName} must be greater than zero.");
        }
    }

    private static void ValidateBufferCapacity(long value, long reservationBytes, string propertyName, List<string> failures)
    {
        if (value < reservationBytes)
        {
            failures.Add($"{propertyName} must accommodate one request/response buffer reservation ({reservationBytes} bytes).");
        }
    }

    private static void ValidateTimeout(TimeSpan value, string propertyName, List<string> failures)
    {
        if (value <= TimeSpan.Zero || value.TotalMilliseconds > int.MaxValue)
        {
            failures.Add($"{propertyName} must be greater than zero and no more than {int.MaxValue} milliseconds.");
        }
    }
}
