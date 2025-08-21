// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
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

        private static IEnumerable<string> EnumerateTokens(
            string source,
            char separator,
            bool trimTokens,
            bool removeEmptyEntries)
        {
            if (string.IsNullOrEmpty(source))
            {
                yield break;
            }

            // Rewritten to avoid ReadOnlySpan<char> across a yield boundary (CS4007).
            int pos = 0;
            int length = source.Length;

            while (pos < length)
            {
                int sepIndex = source.IndexOf(separator, pos);
                int endExclusive = sepIndex >= 0 ? sepIndex : length;

                int tokenStart = pos;
                int tokenEnd = endExclusive - 1;

                if (trimTokens && tokenStart <= tokenEnd)
                {
                    while (tokenStart <= tokenEnd && char.IsWhiteSpace(source[tokenStart]))
                    {
                        tokenStart++;
                    }

                    while (tokenEnd >= tokenStart && char.IsWhiteSpace(source[tokenEnd]))
                    {
                        tokenEnd--;
                    }
                }

                int tokenLength = tokenStart <= tokenEnd ? (tokenEnd - tokenStart + 1) : 0;

                if (!(removeEmptyEntries && tokenLength == 0))
                {
                    string currentToken = tokenLength == 0 ? string.Empty : source.Substring(tokenStart, tokenLength);
                    yield return currentToken;
                }

                if (sepIndex < 0)
                {
                    // No more separators; we're done.
                    break;
                }

                pos = sepIndex + 1;
            }
        }

        public static HashSet<string> ToTokenSet(
                                        this string source,
                                        char separator,
                                        StringComparer comparer = null,
                                        bool trimTokens = true,
                                        bool removeEmptyEntries = true)
        {
            var set = new HashSet<string>(comparer ?? StringComparer.OrdinalIgnoreCase);

            foreach (var token in EnumerateTokens(source, separator, trimTokens, removeEmptyEntries))
            {
                set.Add(token);
            }

            return set;
        }

        /// <summary>
        /// Parses a delimited string into a <see cref="List{String}"/> using the specified separator.
        /// This method avoids intermediate array allocations by enumerating tokens via spans.
        /// </summary>
        /// <param name="source">The delimited string (e.g., "FeatureA,FeatureB"). If null or empty, returns an empty list.</param>
        /// <param name="separator">The character used to separate tokens (e.g., ',').</param>
        /// <param name="comparer">Unused. Present for API symmetry with ToTokenSet.</param>
        /// <param name="trimTokens">If true, trims whitespace around tokens.</param>
        /// <param name="removeEmptyEntries">If true, skips empty tokens.</param>
        /// <returns>A <see cref="List{String}"/> containing the parsed tokens.</returns>
        public static List<string> ToTokenList(
            this string source,
            char separator,
            StringComparer comparer = null,
            bool trimTokens = true,
            bool removeEmptyEntries = true)
        {
            return EnumerateTokens(source, separator, trimTokens, removeEmptyEntries).ToList();
        }
    }
}
