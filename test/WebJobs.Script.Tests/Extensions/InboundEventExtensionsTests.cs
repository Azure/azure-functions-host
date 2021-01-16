// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Eventing.Rpc;
using Xunit;
using static Microsoft.Azure.WebJobs.Script.Grpc.Messages.RpcLog.Types;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class InboundEventExtensionsTests
    {
        [Theory]
        [InlineData(RpcLogCategory.System)]
        [InlineData(RpcLogCategory.User)]
        public void TestLogCategories(RpcLogCategory categoryToTest)
        {
            InboundEvent inboundEvent = new InboundEvent(Guid.NewGuid().ToString(), new Grpc.Messages.StreamingMessage
            {
                RpcLog = new Grpc.Messages.RpcLog
                {
                    LogCategory = categoryToTest,
                }
            });

            Assert.True(inboundEvent.IsLogOfCategory(categoryToTest));
        }
    }
}
