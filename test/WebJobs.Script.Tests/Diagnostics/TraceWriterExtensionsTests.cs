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
        public void WithSource_AppliesDefaultSource()
        {
            TestTraceWriter testTraceWriter = new TestTraceWriter(TraceLevel.Verbose);
            var wrappedTraceWriter = testTraceWriter.WithSource("TestDefault");

            wrappedTraceWriter.Info("Test1");
            wrappedTraceWriter.Info("Test2", "CustomSource");
            wrappedTraceWriter.Info("Test3", string.Empty);

            var traces = testTraceWriter.GetTraces().ToArray();
            Assert.Equal(3, traces.Length);

            Assert.Equal("Test1", traces[0].Message);
            Assert.Equal("TestDefault", traces[0].Source);

            Assert.Equal("Test2", traces[1].Message);
            Assert.Equal("CustomSource", traces[1].Source);

            Assert.Equal("Test3", traces[2].Message);
            Assert.Equal("TestDefault", traces[2].Source);
        }

        [Fact]
        public void Apply_CreatesInterceptingTraceWriter()
        {
            TestTraceWriter traceWriter = new TestTraceWriter(TraceLevel.Verbose);

            traceWriter.Info("Test message");
            var traceEvent = traceWriter.GetTraces().Single();
            Assert.Equal(0, traceEvent.Properties.Count);

            traceWriter.ClearTraces();
            var properties = new Dictionary<string, object>
            {
                { "Foo", 123 },
                { "Bar", 456 }
            };
            var interceptingTraceWriter = traceWriter.Apply(properties);

            interceptingTraceWriter.Info("Test message");
            traceEvent = traceWriter.GetTraces().Single();
            Assert.Equal(2, traceEvent.Properties.Count);
            Assert.Equal(123, traceEvent.Properties["Foo"]);
            Assert.Equal(456, traceEvent.Properties["Bar"]);
        }

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

            Assert.Equal(1, traceWriter.GetTraces().Count);

            var trace = traceWriter.GetTraces().First();
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

            var traces = traceWriter.GetTraces();
            Assert.Equal(1, traces.Count);
            Assert.Equal(level, traces.First().Level);
            Assert.Equal(message, traces.First().Message);
        }
    }
}
