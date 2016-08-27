// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class ConsoleTraceWriterTests
    {
        [Fact]
        public void Trace_AppliesLevelFilter()
        {
            try
            {
                var traceWriter = new ConsoleTraceWriter(TraceLevel.Info);
                using (var writer = new StringWriter())
                {
                    Console.SetOut(writer);

                    traceWriter.Verbose("Test Verbose");
                    traceWriter.Info("Test Info");
                    traceWriter.Warning("Test Warning");
                    traceWriter.Error("Test Error");

                    traceWriter.Flush();

                    string output = writer.ToString();
                    string[] lines = output.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

                    Assert.Equal(3, lines.Length);
                    Assert.Equal("Test Info", lines[0]);
                    Assert.Equal("Test Warning", lines[1]);
                    Assert.Equal("Test Error", lines[2]);
                    Assert.False(output.Contains("Trace Verbose"));
                }
            }
            finally
            {
                var standardOutput = new StreamWriter(Console.OpenStandardOutput());
                standardOutput.AutoFlush = true;
                Console.SetOut(standardOutput);
            }
        }
    }
}
