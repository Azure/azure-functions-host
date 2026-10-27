// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Logging
{
    /// <summary>
    /// Utility class for sanitizing logging strings.
    /// </summary>
    // Note: This file is shared between the WebJobs SDK and Script repos. Update both if changes are needed.
    internal static partial class Sanitizer
    {
        private static readonly string[] CredentialNameFragments = new[] { "password", "pwd", "key", "secret", "token", "sas" };

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
    }
}
