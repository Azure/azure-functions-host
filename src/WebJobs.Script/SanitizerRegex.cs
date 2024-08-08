// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.RegularExpressions;

namespace Microsoft.Azure.WebJobs.Logging
{
    internal partial class SanitizerRegex
    {
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

#if NET8_0_OR_GREATER
        [GeneratedRegex(Pattern, RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace, "en-US")]
        internal static partial Regex Regex();
#else
        private static readonly Regex _urlRegex = new Regex(Pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace);

        internal static Regex Regex() => _urlRegex;
#endif
    }
}
