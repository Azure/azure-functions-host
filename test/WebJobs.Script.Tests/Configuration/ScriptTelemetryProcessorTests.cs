// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
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
        public void Process_UserCodeException_ComputesProblemIdAndPreservesContext()
        {
            var rpcEx = new RpcException(
                "failure",
                "boom",
                "   at My.Namespace.MyFunction.Run(String input) in /src/MyFunction.cs:line 42\n   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)",
                "My.Namespace.MyException",
                isUserException: true);

            var original = new ExceptionTelemetry(new Exception("outer", rpcEx))
            {
                SeverityLevel = SeverityLevel.Critical,
            };
            original.Context.InstrumentationKey = "ikey";
            original.Context.Operation.Id = "op-id";
            original.Context.Operation.Name = "MyFunction";
            original.Context.Operation.ParentId = "parent-id";
            original.Context.Cloud.RoleName = "my-function-app";
            original.Context.Cloud.RoleInstance = "instance-0";
            original.Properties["Category"] = "Host.Results";
            ((ISupportSampling)original).SamplingPercentage = 25;

            var items = new List<ITelemetry>();
            var processor = new ScriptTelemetryProcessor(new TestTelemetryProcessor(items));

            processor.Process(original);

            var result = Assert.IsType<ExceptionTelemetry>(Assert.Single(items));
            Assert.NotSame(original, result);
            Assert.Equal("My.Namespace.MyException at My.Namespace.MyFunction.Run", result.ProblemId);
            Assert.Equal(SeverityLevel.Critical, result.SeverityLevel);
            Assert.Equal("ikey", result.Context.InstrumentationKey);
            Assert.Equal("op-id", result.Context.Operation.Id);
            Assert.Equal("MyFunction", result.Context.Operation.Name);
            Assert.Equal("parent-id", result.Context.Operation.ParentId);
            Assert.Equal("my-function-app", result.Context.Cloud.RoleName);
            Assert.Equal("instance-0", result.Context.Cloud.RoleInstance);
            Assert.Equal(original.Timestamp, result.Timestamp);
            Assert.Equal("Host.Results", result.Properties["Category"]);
            Assert.Equal(25, ((ISupportSampling)result).SamplingPercentage);

            var details = Assert.Single(result.ExceptionDetailsInfoList);
            Assert.Equal("My.Namespace.MyException", details.TypeName);
            Assert.Equal("boom", details.Message);
        }

        [Fact]
        public void Process_UserCodeException_NoParsableStack_ProblemIdFallsBackToTypeName()
        {
            var rpcEx = new RpcException(
                "failure",
                "boom",
                "Traceback (most recent call last):\n  File \"main.py\", line 3, in handler",
                "MyPythonError",
                isUserException: true);

            var items = new List<ITelemetry>();
            var processor = new ScriptTelemetryProcessor(new TestTelemetryProcessor(items));

            processor.Process(new ExceptionTelemetry(new Exception("outer", rpcEx)));

            var result = Assert.IsType<ExceptionTelemetry>(Assert.Single(items));
            Assert.Equal("MyPythonError", result.ProblemId);
        }

        [Fact]
        public void Process_NonUserException_IsPassedThroughUnchanged()
        {
            var rpcEx = new RpcException("failure", "boom", "stack", "My.Type", isUserException: false);
            var original = new ExceptionTelemetry(new Exception("outer", rpcEx));

            var items = new List<ITelemetry>();
            var processor = new ScriptTelemetryProcessor(new TestTelemetryProcessor(items));

            processor.Process(original);

            Assert.Same(original, Assert.Single(items));
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
