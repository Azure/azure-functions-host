// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Azure.WebJobs.ServiceBus.Triggers;
using Microsoft.ServiceBus.Messaging;
using Xunit;

namespace Microsoft.Azure.WebJobs.ServiceBus.UnitTests.Bindings
{
    public class ServiceBusTriggerBindingTests
    {
        [Fact]
        public void CreateBindingDataContract_ReturnsExpectedValue()
        {
            IReadOnlyDictionary<string, Type> argumentContract = null;
            var bindingDataContract = ServiceBusTriggerBinding.CreateBindingDataContract(argumentContract);
            Assert.Equal(12, bindingDataContract.Count);
            Assert.Equal(bindingDataContract["DeliveryCount"], typeof(int));
            Assert.Equal(bindingDataContract["DeadLetterSource"], typeof(string));
            Assert.Equal(bindingDataContract["ExpiresAtUtc"], typeof(DateTime));
            Assert.Equal(bindingDataContract["EnqueuedTimeUtc"], typeof(DateTime));
            Assert.Equal(bindingDataContract["MessageId"], typeof(string));
            Assert.Equal(bindingDataContract["ContentType"], typeof(string));
            Assert.Equal(bindingDataContract["ReplyTo"], typeof(string));
            Assert.Equal(bindingDataContract["SequenceNumber"], typeof(long));
            Assert.Equal(bindingDataContract["To"], typeof(string));
            Assert.Equal(bindingDataContract["Label"], typeof(string));
            Assert.Equal(bindingDataContract["CorrelationId"], typeof(string));
            Assert.Equal(bindingDataContract["Properties"], typeof(IDictionary<string, object>));

            // verify that argument binding values override built ins
            argumentContract = new Dictionary<string, Type>
            {
                { "DeliveryCount", typeof(string) },
                { "NewProperty", typeof(decimal) }
            };
            bindingDataContract = ServiceBusTriggerBinding.CreateBindingDataContract(argumentContract);
            Assert.Equal(13, bindingDataContract.Count);
            Assert.Equal(bindingDataContract["DeliveryCount"], typeof(string));
            Assert.Equal(bindingDataContract["NewProperty"], typeof(decimal));
        }

        [Fact]
        public void CreateBindingData_ReturnsExpectedValue()
        {
            BrokeredMessage message = new BrokeredMessage(new MemoryStream(Encoding.UTF8.GetBytes("Test Message")), true);
            message.ReplyTo = "test-queue";
            message.To = "test";
            message.ContentType = "application/json";
            message.Label = "test label";
            message.CorrelationId = Guid.NewGuid().ToString();
            IReadOnlyDictionary<string, object> valueBindingData = null;
            var bindingData = ServiceBusTriggerBinding.CreateBindingData(message, valueBindingData);
            Assert.Equal(8, bindingData.Count);
            Assert.Equal(message.ReplyTo, bindingData["ReplyTo"]);
            Assert.Equal(message.To, bindingData["To"]);
            Assert.Equal(message.MessageId, bindingData["MessageId"]);
            Assert.Equal(message.DeadLetterSource, bindingData["DeadLetterSource"]);
            Assert.Equal(message.ContentType, bindingData["ContentType"]);
            Assert.Equal(message.Label, bindingData["Label"]);
            Assert.Equal(message.CorrelationId, bindingData["CorrelationId"]);
            Assert.Same(message.Properties, bindingData["Properties"]);

            // verify that value binding data overrides built ins
            valueBindingData = new Dictionary<string, object>
            {
                { "ReplyTo",  "override" },
                { "NewProperty", 123 }
            };
            bindingData = ServiceBusTriggerBinding.CreateBindingData(message, valueBindingData);
            Assert.Equal(9, bindingData.Count);
            Assert.Equal("override", bindingData["ReplyTo"]);
            Assert.Equal(123, bindingData["NewProperty"]);
        }
    }
}
