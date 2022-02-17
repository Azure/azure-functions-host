// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Diagnostics.JitTrace;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.PreJIT
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, TestTraits.ReleaseTests)]
    public class JitPreparesTest
    {
        [Theory]
        [InlineData(WarmUpConstants.JitTraceFileName, 1.0)]
        [InlineData(WarmUpConstants.LinuxJitTraceFileName, 1.0)]
        public void ColdStart_JitFailuresTest(string fileName, double threshold)
        {
            var path = Path.Combine(Path.GetDirectoryName(new Uri(typeof(HostWarmupMiddleware).Assembly.CodeBase).LocalPath), WarmUpConstants.PreJitFolderName, fileName);

            var file = new FileInfo(path);

            Assert.True(file.Exists, $"Expected PGO file '{file.FullName}' does not exist. The file was either renamed or deleted.");
            var lineCount = File.ReadAllLines(path).Length;
            Assert.True(lineCount > 6500, "Jit Trace file line count less than 6500 lines! There is likely a bug removing lines from the linux trace.");
        }
    }
}
