﻿// Copyright (c) .NET Foundation. All rights reserved.
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
    public class JitPreparesTest
    {
        [Theory]
        [InlineData(WarmUpConstants.JitTraceFileName, 1.0)]
        [Trait(TestTraits.Group, TestTraits.ReleaseTests)]
        public void ColdStart_JitFailuresTest_Windows(string fileName, double threshold)
        {
            var path = Path.Combine(Path.GetDirectoryName(new Uri(typeof(HostWarmupMiddleware).Assembly.CodeBase).LocalPath), WarmUpConstants.PreJitFolderName, fileName);

            var file = new FileInfo(path);

            Assert.True(file.Exists, $"Expected PGO file '{file.FullName}' does not exist. The file was either renamed or deleted.");

            JitTraceRuntime.Prepare(file, out int successfulPrepares, out int failedPrepares);

            var failurePercentage = (double)failedPrepares / successfulPrepares * 100;

            // using 1% as approximate number of allowed failures before we need to regenrate a new PGO file.
            Assert.True(failurePercentage < threshold, $"Number of failed PGOs are more than {threshold} percent! Current number of failures are {failedPrepares}. This will definitely impact cold start! Time to regenrate PGOs and update the {fileName} file!");
        }

        [Theory]
        [InlineData(WarmUpConstants.LinuxJitTraceFileName)]
        public void ColdStart_JitFailuresTest_Linux(string fileName)
        {
            var path = Path.Combine(Path.GetDirectoryName(new Uri(typeof(HostWarmupMiddleware).Assembly.CodeBase).LocalPath), WarmUpConstants.PreJitFolderName, fileName);

            var file = new FileInfo(path);

            Assert.True(file.Exists, $"Expected PGO file '{file.FullName}' does not exist. The file was either renamed or deleted.");
            var lineCount = File.ReadAllLines(path).Length;
            Assert.True(lineCount > 6500, $"Jit Trace file line count of {lineCount} for {fileName} is less than 6500 lines! There is likely a bug removing lines from the linux trace.");
        }
    }
}
