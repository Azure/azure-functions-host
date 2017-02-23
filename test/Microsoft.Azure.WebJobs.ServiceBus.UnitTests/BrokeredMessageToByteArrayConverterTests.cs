// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.ServiceBus.Triggers;
using Microsoft.ServiceBus.Messaging;
using Xunit;

namespace Microsoft.Azure.WebJobs.ServiceBus.UnitTests
{
    public class BrokeredMessageToByteArrayConverterTests
    {
        private const string TestString = "This is a test!";

        [Theory]
        [InlineData(ContentTypes.TextPlain)]
        [InlineData(ContentTypes.ApplicationJson)]
        [InlineData(ContentTypes.ApplicationOctetStream)]
        public async Task ConvertAsync_ReturnsExpectedResult_WithStream(string contentType)
        {
            MemoryStream ms = new MemoryStream();
            StreamWriter sw = new StreamWriter(ms);
            sw.Write(TestString);
            sw.Flush();
            ms.Seek(0, SeekOrigin.Begin);

            BrokeredMessage message = new BrokeredMessage(ms);
            message.ContentType = contentType;
            BrokeredMessageToByteArrayConverter converter = new BrokeredMessageToByteArrayConverter();

            byte[] result = await converter.ConvertAsync(message, CancellationToken.None);
            string decoded = Encoding.UTF8.GetString(result);
            Assert.Equal(TestString, decoded);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("some-other-contenttype")]
        public async Task ConvertAsync_Throws_WithStream_UnknownContentType(string contentType)
        {
            MemoryStream ms = new MemoryStream();
            StreamWriter sw = new StreamWriter(ms);
            sw.Write(TestString);
            sw.Flush();
            ms.Seek(0, SeekOrigin.Begin);

            BrokeredMessage message = new BrokeredMessage(ms);
            message.ContentType = contentType;
            BrokeredMessageToByteArrayConverter converter = new BrokeredMessageToByteArrayConverter();

            var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => converter.ConvertAsync(message, CancellationToken.None));

            Assert.IsType<SerializationException>(ex.InnerException);
            string expectedType = contentType ?? "null";          
            Assert.StartsWith(string.Format("The BrokeredMessage with ContentType '{0}' failed to deserialize to a byte[] with the message: ", expectedType), ex.Message);
        }

        [Theory]
        [InlineData(ContentTypes.TextPlain)]
        [InlineData(ContentTypes.ApplicationJson)]
        [InlineData(ContentTypes.ApplicationOctetStream)]
        public async Task ConvertAsync_ReturnsExpectedResult_WithSerializedByteArray_KnownContentType(string contentType)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(TestString);
            BrokeredMessage message = new BrokeredMessage(bytes);
            message.ContentType = contentType;
            BrokeredMessageToByteArrayConverter converter = new BrokeredMessageToByteArrayConverter();

            byte[] result = await converter.ConvertAsync(message, CancellationToken.None);

            // we expect to read back the DataContract-serialized string, since the ContentType tells us to read it back as-is.
            string decoded = Encoding.UTF8.GetString(result);
            Assert.Equal("@base64Binary3http://schemas.microsoft.com/2003/10/Serialization/�This is a test!", decoded);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("some-other-contenttype")]
        public async Task ConvertAsync_ReturnsExpectedResult_WithSerializedByteArray_UnknownContentType(string contentType)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(TestString);
            BrokeredMessage message = new BrokeredMessage(bytes);
            message.ContentType = contentType;
            BrokeredMessageToByteArrayConverter converter = new BrokeredMessageToByteArrayConverter();

            byte[] result = await converter.ConvertAsync(message, CancellationToken.None);

            // we expect to read back the DataContract-serialized string, since the ContentType tells us to read it back as-is.
            string decoded = Encoding.UTF8.GetString(result);
            Assert.Equal(TestString, decoded);
        }

        [Fact]
        public async Task ConvertAsync_Throws_WithSerializedObject()
        {
            BrokeredMessage message = new BrokeredMessage(new TestObject { Text = TestString });

            BrokeredMessageToByteArrayConverter converter = new BrokeredMessageToByteArrayConverter();
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => converter.ConvertAsync(message, CancellationToken.None));

            Assert.IsType<SerializationException>(exception.InnerException);
            Assert.StartsWith("The BrokeredMessage with ContentType 'null' failed to deserialize to a byte[] with the message: ", exception.Message);
        }

        [Serializable]
        public class TestObject
        {
            public string Text { get; set; }
        }
    }
}
