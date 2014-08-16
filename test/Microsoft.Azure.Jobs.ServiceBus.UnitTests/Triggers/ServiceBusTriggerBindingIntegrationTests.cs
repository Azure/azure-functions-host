// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.Azure.Jobs.Host;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Triggers;
using Microsoft.Azure.Jobs.ServiceBus.Triggers;
using Microsoft.ServiceBus.Messaging;
using Xunit;
using Xunit.Extensions;

namespace Microsoft.Azure.Jobs.ServiceBus.UnitTests.Triggers
{
    public class ServiceBusTriggerBindingIntegrationTests
    {
        private ITriggerBinding<BrokeredMessage> _binding;

        public ServiceBusTriggerBindingIntegrationTests()
        {
            IQueueTriggerArgumentBindingProvider provider = new UserTypeArgumentBindingProvider();
            ParameterInfo pi = new StubParameterInfo("parameterName", typeof(UserDataType));
            var argumentBinding = provider.TryCreate(pi);
            _binding = new ServiceBusTriggerBinding("parameterName", argumentBinding, null, "queueName");
        }

        [Theory]
        [InlineData("RequestId", "4b957741-c22e-471d-9f0f-e1e8534b9cb6")]
        [InlineData("RequestReceivedTime", "8/16/2014 12:09:36 AM")]
        [InlineData("DeliveryCount", "8")]
        [InlineData("IsSuccess", "False")]
        public void BindAsync_IfUserDataType_ReturnsValidBindingData(string userPropertyName, string userPropertyValue)
        {
            // Arrange
            UserDataType expectedObject = new UserDataType();
            PropertyInfo userProperty = typeof(UserDataType).GetProperty(userPropertyName);
            var parseMethod = userProperty.PropertyType.GetMethod(
                "Parse",new Type[]{typeof(string)});
            object convertedPropertyValue = parseMethod.Invoke(null, new object[]{userPropertyValue});
            userProperty.SetValue(expectedObject, convertedPropertyValue);
            string messageContent = JsonCustom.SerializeObject(expectedObject);
            BrokeredMessage message = new BrokeredMessage(new MemoryStream(Encoding.UTF8.GetBytes(messageContent)), true);

            ValueBindingContext context = new ValueBindingContext(null, CancellationToken.None);

            // Act
            ITriggerData data = _binding.BindAsync(message, context).GetAwaiter().GetResult();

            // Assert
            Assert.NotNull(data);
            Assert.NotNull(data.ValueProvider);
            Assert.NotNull(data.BindingData);
            Assert.True(data.BindingData.ContainsKey(userPropertyName));
            Assert.Equal(userProperty.GetValue(expectedObject, null), data.BindingData[userPropertyName]);
        }

        private class StubParameterInfo : ParameterInfo
        {
            public StubParameterInfo(string name, Type type)
            {
                NameImpl = name;
                ClassImpl = type;
            }
        }

        public class UserDataType
        {
            public Guid RequestId { get; set; }
            public string BlobFile { get; set; }
            public DateTime RequestReceivedTime { get; set; }
            public Int32 DeliveryCount { get; set; }
            public Boolean IsSuccess { get; set; }
        }
    }
}
