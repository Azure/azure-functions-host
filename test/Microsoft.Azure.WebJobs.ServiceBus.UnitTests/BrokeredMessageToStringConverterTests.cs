// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
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
        public async Task ConvertAsync_ReturnsExpectedResult(string contentType, string value)
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
    }
}
