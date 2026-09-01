// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Globalization;

namespace Azure.Functions.WorkerProxy;

/// <summary>
/// Provides culture-invariant numeric formatting helpers.
/// </summary>
internal static class NumberExtensions
{
    /// <summary>
    /// Formats a 32-bit integer using the invariant culture.
    /// </summary>
    /// <param name="value">The value to format.</param>
    /// <returns>The invariant string representation.</returns>
    public static string ToStringInvariant(this int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Formats a 64-bit integer using the invariant culture.
    /// </summary>
    /// <param name="value">The value to format.</param>
    /// <returns>The invariant string representation.</returns>
    public static string ToStringInvariant(this long value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
