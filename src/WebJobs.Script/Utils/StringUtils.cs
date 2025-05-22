// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Utils
{
    internal static class StringUtils
    {
        /// <summary>
        /// Determines whether a delimited string contains a specific token, using the specified separator and string comparison.
        /// This method is a zero-allocation, faster alternative to splitting the string and using Contains, as it avoids unnecessary allocations.
        /// </summary>
        /// <param name="delimitedString">The string containing delimited tokens to search(Ex: "FeatureA,FeatureB").</param>
        /// <param name="searchToken">The token to search for within the delimited string.</param>
        /// <param name="separator">The character used to separate tokens in the string. Defaults to ','.</param>
        /// <param name="comparisonType">The string comparison type to use. Defaults to OrdinalIgnoreCase.</param>
        /// <returns>True if the token is found; otherwise, false.</returns>
        internal static bool ContainsToken(string delimitedString, string searchToken, char separator = ',', StringComparison comparisonType = StringComparison.OrdinalIgnoreCase)
        {
            if (string.IsNullOrEmpty(delimitedString) || string.IsNullOrEmpty(searchToken))
            {
                return false;
            }

            var remaining = delimitedString.AsSpan();
            var searchSpan = searchToken.AsSpan();

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

                if (currentToken.Equals(searchSpan, comparisonType))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
