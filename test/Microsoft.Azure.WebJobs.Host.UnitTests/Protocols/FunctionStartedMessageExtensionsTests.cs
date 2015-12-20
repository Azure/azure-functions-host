// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Protocols
{
    public class FunctionStartedMessageExtensionsTests
    {
        [Fact]
        public void FormatReason_QueueTriggeredFunction_ReturnsExpectedReason()
        {
            FunctionStartedMessage message = new FunctionStartedMessage();

            QueueTriggerParameterDescriptor triggerParameterDescriptor = new QueueTriggerParameterDescriptor
            {
                Name = "paramName",
                QueueName = "testqueue"
            };
            FunctionDescriptor function = new FunctionDescriptor
            {
                Parameters = new ParameterDescriptor[] { triggerParameterDescriptor }
            };
            message.Function = function;
            message.Reason = ExecutionReason.AutomaticTrigger;

            string result = message.FormatReason();
            Assert.Equal("New queue message detected on 'testqueue'.", result);
        }

        [Fact]
        public void FormatReason_BlobTriggeredFunction_ReturnsExpectedReason()
        {
            FunctionStartedMessage message = new FunctionStartedMessage();
            message.Reason = ExecutionReason.AutomaticTrigger;

            BlobTriggerParameterDescriptor triggerParameterDescriptor = new BlobTriggerParameterDescriptor
            {
                Name = "paramName"
            };
            FunctionDescriptor function = new FunctionDescriptor();
            function.Parameters = new ParameterDescriptor[] { triggerParameterDescriptor };
            message.Function = function;
            message.Arguments = new Dictionary<string, string>() { { "paramName", "blob/path" } };

            string result = message.FormatReason();
            Assert.Equal("New blob detected: blob/path", result);
        }

        [Fact]
        public void FormatReason_ReasonDetailsAlreadySet_ReturnsExpectedReason()
        {
            FunctionStartedMessage message = new FunctionStartedMessage();
            message.ReasonDetails = "The trigger fired!";

            string result = message.FormatReason();
            Assert.Equal("The trigger fired!", result);
        }

        [Fact]
        public void FormatReason_Portal_ReturnsExpectedReason()
        {
            FunctionStartedMessage message = new FunctionStartedMessage();
            message.Reason = ExecutionReason.Portal;

            Assert.Equal("Ran from Portal.", message.FormatReason());

            message.ParentId = Guid.NewGuid();
            Assert.Equal("Replayed from Portal.", message.FormatReason());
        }
    }
}
