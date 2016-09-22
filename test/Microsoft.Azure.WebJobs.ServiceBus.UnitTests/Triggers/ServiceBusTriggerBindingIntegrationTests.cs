// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Microsoft.Azure.WebJobs.ServiceBus.Triggers;
using Microsoft.ServiceBus.Messaging;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Azure.WebJobs.ServiceBus.UnitTests.Triggers
{
    public class ServiceBusTriggerBindingIntegrationTests : IClassFixture<InvariantCultureFixture>
    {
        private ITriggerBinding _queueBinding;
        private ITriggerBinding _topicBinding;

        public ServiceBusTriggerBindingIntegrationTests()
        {
            IQueueTriggerArgumentBindingProvider provider = new UserTypeArgumentBindingProvider();
            ParameterInfo pi = new StubParameterInfo("parameterName", typeof(UserDataType));
            var argumentBinding = provider.TryCreate(pi);
            _queueBinding = new ServiceBusTriggerBinding("parameterName", typeof(UserDataType), argumentBinding, null, AccessRights.Manage, new ServiceBusConfiguration(), "queueName");
            _topicBinding = new ServiceBusTriggerBinding("parameterName", typeof(UserDataType), argumentBinding, null, AccessRights.Manage, new ServiceBusConfiguration(), "subscriptionName", "topicName");
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
            string messageContent = JsonConvert.SerializeObject(expectedObject);
            ValueBindingContext context = new ValueBindingContext(null, CancellationToken.None);

            Action<ITriggerBinding> testBinding = (b) =>
            {
                // Act
                BrokeredMessage message = new BrokeredMessage(new MemoryStream(Encoding.UTF8.GetBytes(messageContent)), true);
                message.ContentType = ContentTypes.ApplicationJson;
                ITriggerData data = _queueBinding.BindAsync(message, context).GetAwaiter().GetResult();

                // Assert
                Assert.NotNull(data);
                Assert.NotNull(data.ValueProvider);
                Assert.NotNull(data.BindingData);
                Assert.True(data.BindingData.ContainsKey(userPropertyName));
                Assert.Equal(userProperty.GetValue(expectedObject, null), data.BindingData[userPropertyName]);
            };

            testBinding(_queueBinding);
            testBinding(_topicBinding);
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
