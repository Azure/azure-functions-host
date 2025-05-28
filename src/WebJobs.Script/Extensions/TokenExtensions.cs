// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;

namespace Microsoft.Azure.WebJobs.Script.Extensions
{
    public static class TokenExtensions
    {
        /// <summary>
        /// Determines whether a delimited string contains a specific token, using the specified separator and string comparison.
        /// This method is a zero-allocation, faster alternative to splitting the string and using Contains, as it avoids unnecessary allocations.
        /// </summary>
        /// <param name="source">The string containing one or more tokens separated by a delimiter (e.g., "FeatureA,FeatureB").</param>
        /// <param name="token">The token to search for. Must not contain the separator character. A match is determined using the specified comparison type.</param>
        /// <param name="separator">The character used to separate tokens in the string. Example ','.</param>
        /// <param name="comparisonType">The string comparison type to use. Defaults to OrdinalIgnoreCase.</param>
        /// <returns>
        /// <c>true</c> if the token is found as an exact match in the delimited string; otherwise, <c>false</c>.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// Thrown if <paramref name="token"/> contains the separator character.
        /// </exception>
        /// <remarks>
        /// If <paramref name="source"/> is empty or <paramref name="token"/> is empty, the method returns <c>false</c>.
        /// </remarks>
        public static bool ContainsToken(this string source, string token, char separator, StringComparison comparisonType = StringComparison.OrdinalIgnoreCase)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(token))
            {
                return false;
            }

            return source.AsSpan().ContainsToken(token.AsSpan(), separator, comparisonType);
        }

        /// <summary>
        /// Determines whether a delimited <see cref="ReadOnlySpan{Char}"/> contains a specific token,
        /// using the specified separator and string comparison. This method is a high-performance,
        /// zero-allocation alternative that avoids splitting or heap allocations.
        /// </summary>
        /// <param name="source">The span containing one or more tokens separated by a delimiter (e.g., "FeatureA,FeatureB").</param>
        /// <param name="token">The token to search for. Must not contain the separator character. A match is determined using the specified comparison type.</param>
        /// <param name="separator">The character used to separate tokens in the span. Example ','.</param>
        /// <param name="comparisonType">The string comparison type to use. Defaults to OrdinalIgnoreCase.</param>
        /// <returns>
        /// <c>true</c> if the token is found as an exact match in the delimited span; otherwise, <c>false</c>.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// Thrown if <paramref name="token"/> contains the separator character.
        /// </exception>
        /// <remarks>
        /// If <paramref name="source"/> is empty or <paramref name="token"/> is empty, the method returns <c>false</c>.
        /// </remarks>
        public static bool ContainsToken(this ReadOnlySpan<char> source, ReadOnlySpan<char> token, char separator, StringComparison comparisonType = StringComparison.OrdinalIgnoreCase)
        {
            if (token.IsEmpty)
            {
                return false;
            }

            if (token.Contains(separator))
            {
                throw new ArgumentException($"The search token must not contain the separator character '{separator}'.", nameof(token));
            }

            var remaining = source;

            while (!remaining.IsEmpty)
            {
                var separatorIndex = remaining.IndexOf(separator);
                ReadOnlySpan<char> currentToken;

                if (separatorIndex >= 0)
                {
                    currentToken = remaining.Slice(0, separatorIndex);
                    remaining = remaining.Slice(separatorIndex + 1);
                }
                else
                {
                    currentToken = remaining;
                    remaining = default;
                }

                if (currentToken.Equals(token, comparisonType))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
