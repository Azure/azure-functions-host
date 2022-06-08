// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class ScriptTelemetryProcessorTests
    {
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
