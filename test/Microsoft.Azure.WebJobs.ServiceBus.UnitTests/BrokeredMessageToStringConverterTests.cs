// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.ServiceBus.Triggers;
using Microsoft.ServiceBus.Messaging;
using Xunit;

namespace Microsoft.Azure.WebJobs.ServiceBus.UnitTests
{
    public class BrokeredMessageToStringConverterTests
    {
        private const string TestString = "This is a test!";
        private const string TestJson = "{ value: 'This is a test!' }";

        [Theory]
        [InlineData(ContentTypes.TextPlain, TestString)]
        [InlineData(ContentTypes.ApplicationJson, TestJson)]
        [InlineData(ContentTypes.ApplicationOctetStream, TestString)]
        [InlineData(null, TestJson)]
        [InlineData("application/xml", TestJson)]
        public async Task ConvertAsync_ReturnsExpectedResult_WithStream(string contentType, string value)
        {
            MemoryStream ms = new MemoryStream();
            StreamWriter sw = new StreamWriter(ms);
            sw.Write(value);
            sw.Flush();
            ms.Seek(0, SeekOrigin.Begin);

            BrokeredMessage message = new BrokeredMessage(ms);
            message.ContentType = contentType;

            BrokeredMessageToStringConverter converter = new BrokeredMessageToStringConverter();
            string result = await converter.ConvertAsync(message, CancellationToken.None);

            Assert.Equal(value, result);
        }

        [Theory]
        [InlineData(ContentTypes.TextPlain, TestString)]
        [InlineData(ContentTypes.ApplicationJson, TestJson)]
        [InlineData(ContentTypes.ApplicationOctetStream, TestString)]
        [InlineData(null, TestJson)]
        [InlineData("application/xml", TestJson)]
        public async Task ConvertAsync_ReturnsExpectedResult_WithSerializedString(string contentType, string value)
        {
            BrokeredMessage message = new BrokeredMessage(value);
            message.ContentType = contentType;

            BrokeredMessageToStringConverter converter = new BrokeredMessageToStringConverter();
            string result = await converter.ConvertAsync(message, CancellationToken.None);
            Assert.Equal(value, result);
        }

        [Fact]
        public async Task ConvertAsync_Throws_WithSerializedObject()
        {
            BrokeredMessage message = new BrokeredMessage(new TestObject { Text = TestString });

            BrokeredMessageToStringConverter converter = new BrokeredMessageToStringConverter();
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => converter.ConvertAsync(message, CancellationToken.None));

            Assert.IsType<SerializationException>(exception.InnerException);
            Assert.StartsWith("The BrokeredMessage with ContentType 'null' failed to deserialize to a string with the message:", exception.Message);
        }

        [Serializable]
        public class TestObject
        {
            public string Text { get; set; }
        }
    }
}
