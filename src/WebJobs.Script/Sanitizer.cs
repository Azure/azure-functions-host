// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Logging
{
    /// <summary>
    /// Utility class for sanitizing logging strings.
    /// </summary>
    // Note: This file is shared between the WebJobs SDK and Script repos. Update both if changes are needed.
    internal static class Sanitizer
    {
        public const string SecretReplacement = "[Hidden Credential]";
        private static readonly char[] ValueTerminators = new char[] { '<', '"', '\'' };

        // List of keywords that should not be replaced with [Hidden Credential]
        private static readonly string[] AllowedTokens = new string[] { "PublicKeyToken=" };
        internal static readonly string[] CredentialTokens = new string[] { "Token=", "DefaultEndpointsProtocol=http", "AccountKey=", "Data Source=", "Server=", "Password=", "pwd=", "&amp;sig=", "&sig=", "?sig=", "SharedAccessKey=" };

        /// <summary>
        /// Removes well-known credential strings from strings.
        /// </summary>
        /// <param name="input">The string to sanitize.</param>
        /// <returns>The sanitized string.</returns>
        internal static string Sanitize(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            // Everything we *might* replace contains an equal, so if we don't have that short circuit out.
            // This can be likely be more efficient with a Regex, but that's best done with a large test suite and this is
            // a quick/simple win for the high traffic case.
            if (!MayContainCredentials(input))
            {
                return input;
            }

            string t = input;
            string inputWithAllowedTokensHidden = input;

            // Remove any known safe strings from the input before looking for Credentials
            foreach (string allowedToken in AllowedTokens)
            {
                if (inputWithAllowedTokensHidden.Contains(allowedToken))
                {
                    string hiddenString = new string('#', allowedToken.Length);
                    inputWithAllowedTokensHidden = inputWithAllowedTokensHidden.Replace(allowedToken, hiddenString);
                }
            }

            foreach (var token in CredentialTokens)
            {
                int startIndex = 0;
                while (true)
                {
                    // search for the next token instance
                    startIndex = inputWithAllowedTokensHidden.IndexOf(token, startIndex, StringComparison.OrdinalIgnoreCase);
                    if (startIndex == -1)
                    {
                        break;
                    }

                    // Find the end of the secret. It most likely ends with either a double quota " or tag opening <
                    int credentialEnd = t.IndexOfAny(ValueTerminators, startIndex);

                    t = t.Substring(0, startIndex) + SecretReplacement + (credentialEnd != -1 ? t.Substring(credentialEnd) : string.Empty);
                    inputWithAllowedTokensHidden = inputWithAllowedTokensHidden.Substring(0, startIndex) + SecretReplacement + (credentialEnd != -1 ? inputWithAllowedTokensHidden.Substring(credentialEnd) : string.Empty);
                }
            }

            return t;
        }

        /// <summary>
        /// Checks if a string even *possibly* contains one of our <see cref="CredentialTokens"/>.
        /// Useful for short-circuiting more expensive checks and replacements if it's known we wouldn't do anything.
        /// </summary>
        internal static bool MayContainCredentials(string input) => input.Contains("=");
    }
}