// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public class FunctionTraceWriterFactoryTests
    {
        private string _logPath;

        public FunctionTraceWriterFactoryTests()
        {
            _logPath = Path.Combine(Path.GetTempPath(), "WebJobs.Script.Tests", nameof(FunctionTraceWriterFactoryTests));

            // Try to clean up. This may fail if the directory is open in Explorer, for example.
            try
            {
                FileUtility.DeleteDirectoryAsync(_logPath, true).GetAwaiter().GetResult();
            }
            catch
            {
            }
        }

        [Fact]
        public void Factory_CachesTraceWriters_CaseInsensitive()
        {
            var config = new ScriptHostConfiguration();
            var factory = new FunctionTraceWriterFactory(config, t => true);

            var writer1 = factory.Create("TestFunction1");
            var writer2 = factory.Create("TestFunction1");

            var writer3 = factory.Create("TestFunction2");
            var writer4 = factory.Create("testfunction2");

            Assert.Same(writer1, writer2);
            Assert.Same(writer3, writer4);
            Assert.NotSame(writer1, writer3);
        }

        [Fact]
        public void Factory_Dispose_RemovesTraceWriter()
        {
            var config = new ScriptHostConfiguration();
            var factory = new FunctionTraceWriterFactory(config, t => true);

            var writer1 = factory.Create("TestFunction1");
            var writer2 = factory.Create("TestFunction2");

            // This will remove the writer from the parent factory's cache.
            (writer1 as IDisposable).Dispose();

            var writer3 = factory.Create("TestFunction1");
            var writer4 = factory.Create("TestFunction2");

            Assert.NotSame(writer1, writer3);
            Assert.Same(writer2, writer4);
        }

        [Fact]
        public void Factory_AppliesFilter()
        {
            var config = new ScriptHostConfiguration()
            {
                RootLogPath = _logPath
            };

            string functionName = Guid.NewGuid().ToString();

            var metricsLogger = new TestMetricsLogger();
            config.HostConfig.AddService<IMetricsLogger>(metricsLogger);

            bool fileLoggingEnabled = true;

            var userTraceProp = new Dictionary<string, object>
            {
                { ScriptConstants.TracePropertyIsUserTraceKey, true }
            };

            var factory = new FunctionTraceWriterFactory(config, t => fileLoggingEnabled);
            var writer = factory.Create(functionName);

            writer.Info("Test 1");
            writer.Info("Test 2", userTraceProp);

            fileLoggingEnabled = false;

            writer.Info("Test 3");
            writer.Info("Test 4", userTraceProp);

            fileLoggingEnabled = true;

            writer.Info("Test 5");
            writer.Info("Test 6", userTraceProp);

            writer.Flush();

            string file = Directory.EnumerateFiles(Path.Combine(_logPath, "Function", functionName)).Single();
            var lines = File.ReadAllLines(file).ToArray();
            Assert.Equal(4, lines.Length);
            Assert.EndsWith("Test 1", lines[0]);
            Assert.EndsWith("Test 2", lines[1]);
            Assert.EndsWith("Test 5", lines[2]);
            Assert.EndsWith("Test 6", lines[3]);

            // Verify the metric counts were applied for the user traces
            Assert.Equal(3, metricsLogger.LoggedEvents.Count);
        }
    }
}
