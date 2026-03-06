// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public static partial class TestHelpers
    {
        /// <summary>
        /// Asserts that an options type with the given name appears in the collected options log entries.
        /// Each entry is formatted as "OptionTypeName\n{ json }", so matching is done on the first line.
        /// </summary>
        public static void AssertOptionLogged(List<string> allOptionsLogs, string optionName)
        {
            var loggedOptionNames = allOptionsLogs.Select(GetOptionTypeName).ToList();

            Assert.True(
                allOptionsLogs.Any(m => m.StartsWith(optionName, StringComparison.Ordinal)),
                $"{optionName} not found. Logged options: [{string.Join(", ", loggedOptionNames)}]");
        }

        /// <summary>
        /// Extracts the options type name from a formatted options log entry.
        /// Each entry is formatted as "OptionTypeName\n{ json }", so the type name is the first line.
        /// </summary>
        public static string GetOptionTypeName(string optionsLogEntry)
        {
            var firstLineEnd = optionsLogEntry.IndexOfAny(['\r', '\n']);

            return firstLineEnd >= 0
                ? optionsLogEntry[..firstLineEnd]
                : optionsLogEntry;
        }
    }
}
