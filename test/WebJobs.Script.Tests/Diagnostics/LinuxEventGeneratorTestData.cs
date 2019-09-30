// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public static class LinuxEventGeneratorTestData
    {
        public static IEnumerable<object[]> GetDetailsEvents()
        {
            yield return new object[] { "TestApp", "TestFunction", "{ foo: 123, bar: \"Test\" }", "{ foo: \"Mathew\", bar: \"Charles\" }", "CSharp", 0 };
            yield return new object[] { string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, 0 };
        }

        public static IEnumerable<object[]> GetMetricEvents()
        {
            yield return new object[] { "C37E3412-86D1-4B93-BC5A-A2AE09D26C2D", "TestApp", "TestFunction", "TestEvent", 15, 2, 18, 5, "TestData", "TestRuntimeSiteName" };
            yield return new object[] { string.Empty, string.Empty, string.Empty, string.Empty, 0, 0, 0, 0, string.Empty, string.Empty };
        }

        public static IEnumerable<object[]> GetLogEvents()
        {
            yield return new object[] { LogLevel.Information, "C37E3412-86D1-4B93-BC5A-A2AE09D26C2D", "TestApp", "TestFunction", "TestEvent", "TestSource", "These are the details, lots of details", "This is the summary, a great summary", "TestExceptionType", "Test exception message, with details", "E2D5A6ED-4CE3-4CFD-8878-FD4814F0A1F3", "3AD41658-1C4E-4C9D-B0B9-24F2BDAE2829", "F0AAA9AD-C3A6-48B9-A75E-57BB280EBB53", "TestRuntimeSiteName" };
            yield return new object[] { LogLevel.Information, string.Empty, "TestApp", "TestFunction", "TestEvent", string.Empty, "This string includes a quoted \"substring\" in the middle", "Another \"quoted substring\"", "TestExceptionType", "Another \"quoted substring\"", "E2D5A6ED-4CE3-4CFD-8878-FD4814F0A1F3", "3AD41658-1C4E-4C9D-B0B9-24F2BDAE2829", "F0AAA9AD-C3A6-48B9-A75E-57BB280EBB53", "TestRuntimeSiteName" };
            yield return new object[] { LogLevel.Information, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty };
        }
    }
}
