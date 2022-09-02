// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Eventing;
using Xunit;
using static Microsoft.Azure.WebJobs.Script.Grpc.Messages.RpcLog.Types;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class InboundGrpcEventExtensionsTests
    {
        [Theory]
        [InlineData(RpcLogCategory.System)]
        [InlineData(RpcLogCategory.User)]
        [InlineData(RpcLogCategory.CustomMetric)]
        public void TestLogCategories(RpcLogCategory categoryToTest)
        {
            InboundGrpcEvent inboundEvent = new InboundGrpcEvent(Guid.NewGuid().ToString(), new Grpc.Messages.StreamingMessage
            {
                RpcLog = new Grpc.Messages.RpcLog
                {
                    LogCategory = categoryToTest,
                }
            });

            Assert.True(inboundEvent.Message.RpcLog.LogCategory == categoryToTest);
        }
    }
}
