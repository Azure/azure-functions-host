using System.Globalization;

namespace Microsoft.Azure.Functions.WorkerProxy;

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
    public static string ToStringInvariant(this int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Formats a 64-bit integer using the invariant culture.
    /// </summary>
    /// <param name="value">The value to format.</param>
    /// <returns>The invariant string representation.</returns>
    public static string ToStringInvariant(this long value) => value.ToString(CultureInfo.InvariantCulture);
}
