// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class ScriptTelemetryProcessorTests
    {
        [Theory]
        [InlineData("Http")]
        [InlineData("HTTP")]
        [InlineData("http")]
        public void Process_HttpDependency_ProxyRequest_IsFiltered(string type)
        {
            var items = new List<ITelemetry>();
            var processor = new ScriptTelemetryProcessor(new TestTelemetryProcessor(items));

            ScriptTelemetryProcessor.SuppressDependencyTelemetry.Value = true;
            try
            {
                processor.Process(new DependencyTelemetry { Type = type });
            }
            finally
            {
                ScriptTelemetryProcessor.SuppressDependencyTelemetry.Value = false;
            }

            Assert.Empty(items);
        }

        [Theory]
        [InlineData("Http")]
        [InlineData("HTTP")]
        [InlineData("http")]
        public void Process_HttpDependency_NotProxyRequest_IsNotFiltered(string type)
        {
            var items = new List<ITelemetry>();
            var processor = new ScriptTelemetryProcessor(new TestTelemetryProcessor(items));

            // SuppressDependencyTelemetry defaults to false — simulates user code making an external HTTP call
            processor.Process(new DependencyTelemetry { Type = type });

            Assert.Single(items);
        }

        [Fact]
        public void Process_NonDependencyTelemetry_ProxyRequest_IsNotFiltered()
        {
            var items = new List<ITelemetry>();
            var processor = new ScriptTelemetryProcessor(new TestTelemetryProcessor(items));

            ScriptTelemetryProcessor.SuppressDependencyTelemetry.Value = true;
            try
            {
                processor.Process(new TraceTelemetry("test"));
            }
            finally
            {
                ScriptTelemetryProcessor.SuppressDependencyTelemetry.Value = false;
            }

            Assert.Single(items);
        }

        [Fact]
        public async Task Test_TelemetryProcessor_AppInsights()
        {
            var rpcEx = new RpcException("failed", "user message", "user stack", "user exception type");
            rpcEx.IsUserException = true;

            TelemetryConfiguration config = new TelemetryConfiguration("instrumentation key");
            ExceptionTelemetry oldEt = new ExceptionTelemetry(rpcEx);
            config.TelemetryProcessorChainBuilder.Use(next => new MyCustomTelemetryProcessor(next));
            TelemetryClient client = new TelemetryClient(config);
            client.TrackException(oldEt);
            await client.FlushAsync(CancellationToken.None);
        }

        private class TestTelemetryProcessor : ITelemetryProcessor
        {
            private readonly List<ITelemetry> _items;

            public TestTelemetryProcessor(List<ITelemetry> items)
            {
                _items = items;
            }

            public void Process(ITelemetry item)
            {
                _items.Add(item);
            }
        }

        public class MyCustomTelemetryProcessor : ITelemetryProcessor
        {
            public MyCustomTelemetryProcessor(ITelemetryProcessor item)
            {
                this.Next = item;
            }

            private ITelemetryProcessor Next { get; set; }

            public void Process(ITelemetry item)
            {
                if (item is ExceptionTelemetry exceptionTelemetry
                    && exceptionTelemetry.Exception is RpcException rpcException
                    && rpcException.IsUserException)
                {
                    item = ToUserException(rpcException, item);
                }
                this.Next.Process(item);
            }

            private ITelemetry ToUserException(RpcException rpcException, ITelemetry originalItem)
            {
                rpcException.RemoteTypeName = "test user exception type";

                string typeName = string.IsNullOrEmpty(rpcException.RemoteTypeName) ? rpcException.GetType().ToString() : rpcException.RemoteTypeName;

                var userExceptionDetails = new ExceptionDetailsInfo(1, -1, typeName, rpcException.RemoteMessage, true, rpcException.RemoteStackTrace, new StackFrame[] { });

                ExceptionTelemetry newET = new ExceptionTelemetry(new[] { userExceptionDetails },
                SeverityLevel.Error, "ProblemId",
                new Dictionary<string, string>() { },
                new Dictionary<string, double>() { });

                newET.Context.InstrumentationKey = originalItem.Context.InstrumentationKey;
                newET.Timestamp = originalItem.Timestamp;

                return newET;
            }
        }
    }
}
