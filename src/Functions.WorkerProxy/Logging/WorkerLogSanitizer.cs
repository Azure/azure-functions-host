// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Text.RegularExpressions;

namespace Azure.Functions.WorkerProxy.Logging;

internal static partial class WorkerLogSanitizer
{
    private const string SecretReplacement = "[Hidden Credential]";
    private static readonly char[] ValueTerminators = ['<', '"', '\''];
    private static readonly string[] AllowedTokens = ["PublicKeyToken="];
    private static readonly string[] CredentialTokens =
    [
        "Token=", "DefaultEndpointsProtocol=http", "AccountKey=", "Data Source=", "Server=", "Password=", "pwd=",
        "&amp;sig=", "&sig=", "?sig=", "SharedAccessKey=", "&amp;code=", "&code=", "?code=", "/code=", "key="
    ];

    public static string Sanitize(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        if (!input.Contains('=') && !input.Contains(':'))
        {
            return input;
        }

        string sanitized = input;
        string inputWithAllowedTokensHidden = input;
        foreach (string allowedToken in AllowedTokens)
        {
            if (inputWithAllowedTokensHidden.Contains(allowedToken, StringComparison.Ordinal))
            {
                inputWithAllowedTokensHidden = inputWithAllowedTokensHidden.Replace(
                    allowedToken, new string('#', allowedToken.Length), StringComparison.Ordinal);
            }
        }

        foreach (string token in CredentialTokens)
        {
            int startIndex = 0;
            while (true)
            {
                startIndex = inputWithAllowedTokensHidden.IndexOf(token, startIndex, StringComparison.OrdinalIgnoreCase);
                if (startIndex == -1)
                {
                    break;
                }

                int credentialEnd = sanitized.IndexOfAny(ValueTerminators, startIndex);
                sanitized = sanitized[..startIndex] + SecretReplacement
                    + (credentialEnd != -1 ? sanitized[credentialEnd..] : string.Empty);
                inputWithAllowedTokensHidden = inputWithAllowedTokensHidden[..startIndex] + SecretReplacement
                    + (credentialEnd != -1 ? inputWithAllowedTokensHidden[credentialEnd..] : string.Empty);
            }
        }

        return input.Contains(':', StringComparison.Ordinal)
            ? CredentialUriRegex().Replace(sanitized, SecretReplacement)
            : sanitized;
    }

    [GeneratedRegex(
        @"\b([a-zA-Z]+):\/\/([^:/\s]+):([^@/\s]+)@([^:/\s]+):([0-9]+)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex CredentialUriRegex();
}