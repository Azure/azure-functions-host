// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    /// <summary>
    /// Helpers for metrics.
    /// </summary>
    internal static class MetricHelpers
    {
        /// <summary>
        /// Convert an unknown struct type to a double, avoiding boxing.
        /// </summary>
        /// <param name="value">The value to convert.</param>
        /// <typeparam name="TIn">The input type.</typeparam>
        /// <returns>The converted value.</returns>
        public static double ConvertToDouble<TIn>(TIn value)
            where TIn : struct
        {
            return value switch
            {
                byte b => Convert.ToDouble(b),
                short s => Convert.ToDouble(s),
                int i => Convert.ToDouble(i),
                long l => Convert.ToDouble(l),
                float f => Convert.ToDouble(f),
                double d => d,
                decimal d => Convert.ToDouble(d),
                _ => throw new ArgumentException($"Unsupported type: {typeof(TIn)}", nameof(value)),
            };
        }

        /// <summary>
        /// Convert an unknown struct type to a long, avoiding boxing.
        /// </summary>
        /// <param name="value">The value to convert.</param>
        /// <typeparam name="TIn">The input type.</typeparam>
        /// <returns>The converted value.</returns>
        public static long ConvertToLong<TIn>(TIn value)
            where TIn : struct
        {
            return value switch
            {
                byte b => Convert.ToInt64(b),
                short s => Convert.ToInt64(s),
                int i => Convert.ToInt64(i),
                long l => l,
                float f => Convert.ToInt64(f),
                double d => Convert.ToInt64(d),
                decimal d => Convert.ToInt64(d),
                _ => throw new ArgumentException($"Unsupported type: {typeof(TIn)}", nameof(value)),
            };
        }
    }
}