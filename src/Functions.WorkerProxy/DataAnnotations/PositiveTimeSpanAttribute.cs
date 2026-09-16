// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.ComponentModel.DataAnnotations;

namespace Azure.Functions.WorkerProxy.DataAnnotations;

/// <summary>
/// Validates that a <see cref="TimeSpan"/> is greater than zero.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
internal sealed class PositiveTimeSpanAttribute : ValidationAttribute
{
    public PositiveTimeSpanAttribute()
        : base("{0} must be greater than zero.")
    {
    }

    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        return value is TimeSpan timeSpan && timeSpan > TimeSpan.Zero;
    }
}
