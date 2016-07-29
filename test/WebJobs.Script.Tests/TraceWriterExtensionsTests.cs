// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Azure.WebJobs.Host;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class TraceWriterExtensionsTests
    {
        [Fact]
        public void Verbose_TracesMessageAndCorrectTraceLevel()
        {
            var expectedLevel = TraceLevel.Verbose;
            string expectedMessage = Guid.NewGuid().ToString();
            TestMessageAndLevel(t => t.Verbose(expectedMessage, new Dictionary<string, object>()), expectedMessage, expectedLevel);
        }

        [Fact]
        public void Info_TracesMessageAndCorrectTraceLevel()
        {
            var expectedLevel = TraceLevel.Info;
            string expectedMessage = Guid.NewGuid().ToString();
            TestMessageAndLevel(t => t.Info(expectedMessage, new Dictionary<string, object>()), expectedMessage, expectedLevel);
        }

        [Fact]
        public void Warning_TracesMessageAndCorrectTraceLevel()
        {
            var expectedLevel = TraceLevel.Warning;
            string expectedMessage = Guid.NewGuid().ToString();
            TestMessageAndLevel(t => t.Warning(expectedMessage, new Dictionary<string, object>()), expectedMessage, expectedLevel);
        }

        [Fact]
        public void Error_TracesMessageAndCorrectTraceLevel()
        {
            var expectedLevel = TraceLevel.Error;
            string expectedMessage = Guid.NewGuid().ToString();
            TestMessageAndLevel(t => t.Error(expectedMessage, new Dictionary<string, object>()), expectedMessage, expectedLevel);
        }

        [Theory]
        [InlineData("verbose", TraceLevel.Verbose)]
        [InlineData("info", TraceLevel.Info)]
        [InlineData("warning", TraceLevel.Warning)]
        [InlineData("error", TraceLevel.Error)]
        public void Trace_TracesMessageAndCorrectTraceLevel(string message, TraceLevel level)
        {
            TestMessageAndLevel(t => t.Trace(message, level, new Dictionary<string, object>()), message, level);
        }

        [Fact]
        public void PropertiesAreProperlySet()
        {
            var traceWriter = new TestTraceWriter(TraceLevel.Verbose);

            var properties = new Dictionary<string, object>
            {
                { "prop1", "prop1" },
                { "prop2", "prop2" },
                { "prop3", "prop3" }
            };

            traceWriter.Trace("test", TraceLevel.Verbose, properties);

            Assert.Equal(1, traceWriter.Traces.Count);

            var trace = traceWriter.Traces.First();
            foreach (var property in properties)
            {
                Assert.True(trace.Properties.ContainsKey(property.Key));
                Assert.Equal(property.Value, trace.Properties[property.Key]);
            }
        }

        private void TestMessageAndLevel(Action<TraceWriter> traceHandler, string message, TraceLevel level)
        {
            var traceWriter = new TestTraceWriter(level);
            traceHandler(traceWriter);

            Assert.Equal(1, traceWriter.Traces.Count);
            Assert.Equal(level, traceWriter.Traces.First().Level);
            Assert.Equal(message, traceWriter.Traces.First().Message);
        }
    }
}
