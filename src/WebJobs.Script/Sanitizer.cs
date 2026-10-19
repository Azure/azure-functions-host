// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Logging
{
    /// <summary>
    /// Utility class for sanitizing logging strings.
    /// </summary>
    // Note: This file is shared between the WebJobs SDK and Script repos. Update both if changes are needed.
    internal static partial class Sanitizer
    {
        public const string SecretReplacement = "[Hidden Credential]";
        public const string EmailReplacement = "[Hidden Email]";
        internal const string EmailSanitizationTimeoutReplacement = "[Redacted: email sanitization timed out]";
        private const int EmailRegexTimeoutMilliseconds = 1000;

        // Pattern of format : "<local-part>@<domain>.<tld>"
        private const string EmailPattern = @"
                                                [a-zA-Z0-9._%+-]+        # Capture local part
                                                @                        # '@'
                                                [a-zA-Z0-9-]+            # Capture the first domain label
                                                (?:\.[a-zA-Z0-9-]+)*     # Capture any additional domain labels
                                                \.[a-zA-Z]{2,}           # Capture the top level domain
                                            ";

        private static readonly char[] ValueTerminators = new char[] { '<', '"', '\'' };

        // List of keywords that should not be replaced with [Hidden Credential]
        private static readonly string[] AllowedTokens = new string[] { "PublicKeyToken=" };
        internal static readonly string[] CredentialTokens = new string[] { "Token=", "DefaultEndpointsProtocol=http", "AccountKey=", "Data Source=", "Server=", "Password=", "pwd=", "&amp;sig=", "&sig=", "?sig=", "SharedAccessKey=", "&amp;code=", "&code=", "?code=", "/code=", "key=" };
        private static readonly string[] CredentialNameFragments = new[] { "password", "pwd", "key", "secret", "token", "sas" };

        // Pattern of format : "<protocol>://<username>:<password>@<address>:<port>"
        private static readonly string Pattern = @"
                                                \b([a-zA-Z]+)            # Capture protocol
                                                :\/\/                    # '://'
                                                ([^:/\s]+)               # Capture username
                                                :                        # ':'
                                                ([^@/\s]+)               # Capture password
                                                @                        # '@'
                                                ([^:/\s]+)               # Capture address
                                                :                        # ':'
                                                ([0-9]+)\b               # Capture port number
                                            ";

        private static readonly Regex Regex = new Regex(Pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace);

        private static readonly Func<string, string, string> DefaultEmailReplacer =
            static (input, replacement) => EmailRegex().Replace(input, replacement);

        [GeneratedRegex(EmailPattern, RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace, EmailRegexTimeoutMilliseconds)]
        internal static partial Regex EmailRegex();

        /// <summary>
        /// Removes well-known credential strings and email addresses from strings.
        /// </summary>
        /// <param name="input">The string to sanitize.</param>
        /// <returns>The sanitized string.</returns>
        internal static string Sanitize(string input)
        {
            return Sanitize(input, DefaultEmailReplacer);
        }

        internal static string Sanitize(string input, Func<string, string, string> emailReplacer)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            // Everything we *might* replace contains an equal, a colon or an '@', so if we don't have any of those
            // short circuit out. This can be likely be more efficient with a Regex, but that's best done with a large
            // test suite and this is a quick/simple win for the high traffic case.
            if (!MayContainCredentials(input) && !MayContainEmail(input))
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

            // This check avoids unnecessary regex evaluation if the input does not contain any url
            if (input.Contains(":"))
            {
                t = Regex.Replace(t, SecretReplacement);
            }

            // Credentials are redacted above, so "<protocol>://<user>:<password>@<host>:<port>" is already gone.
            if (MayContainEmail(t))
            {
                try
                {
                    t = emailReplacer(t, EmailReplacement);
                }
                catch (RegexMatchTimeoutException)
                {
                    t = EmailSanitizationTimeoutReplacement;
                }
            }

            return t;
        }

        internal static JObject Sanitize(JObject obj, Func<string, bool> selector = null)
        {
            return Sanitize(obj, selector, DefaultEmailReplacer);
        }

        internal static JObject Sanitize(JObject obj, Func<string, bool> selector, Func<string, string, string> emailReplacer)
        {
            static bool IsPotentialCredential(string name)
            {
                foreach (string fragment in CredentialNameFragments)
                {
                    if (name.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            }

            static JToken Sanitize(JToken token, Func<string, string, string> emailReplacer)
            {
                if (token is JObject obj)
                {
                    JObject sanitized = new JObject();
                    foreach (var prop in obj)
                    {
                        if (IsPotentialCredential(prop.Key))
                        {
                            sanitized[prop.Key] = Sanitizer.SecretReplacement;
                        }
                        else
                        {
                            sanitized[prop.Key] = Sanitize(prop.Value, emailReplacer);
                        }
                    }

                    return sanitized;
                }

                if (token is JArray arr)
                {
                    JArray sanitized = new JArray();
                    foreach (var value in arr)
                    {
                        sanitized.Add(Sanitize(value, emailReplacer));
                    }

                    return sanitized;
                }

                if (token.Type == JTokenType.String)
                {
                    return Sanitizer.Sanitize(token.ToString(), emailReplacer);
                }

                return token;
            }

            JObject sanitizedObject = new JObject();
            foreach (var prop in obj)
            {
                string propName = prop.Key;
                if (selector != null && !selector(propName))
                {
                    continue;
                }

                var propValue = prop.Value;
                if (propValue != null)
                {
                    sanitizedObject[propName] = Sanitize(propValue, emailReplacer);
                }
            }

            return sanitizedObject;
        }

        /// <summary>
        /// Checks if a string even *possibly* contains one of our <see cref="CredentialTokens"/>.
        /// Useful for short-circuiting more expensive checks and replacements if it's known we wouldn't do anything.
        /// </summary>
        internal static bool MayContainCredentials(string input) => input.Contains("=") || input.Contains(":");

        /// <summary>
        /// Checks if a string even *possibly* contains an email address.
        /// Useful for short-circuiting the more expensive email replacement if it's known we wouldn't do anything.
        /// This is deliberately separate from <see cref="MayContainCredentials(string)"/>: a bare email address such as
        /// "someone@contoso.com" contains neither '=' nor ':', and an email is not a credential.
        /// </summary>
        internal static bool MayContainEmail(string input) => input.IndexOf('@') >= 0;
    }
}