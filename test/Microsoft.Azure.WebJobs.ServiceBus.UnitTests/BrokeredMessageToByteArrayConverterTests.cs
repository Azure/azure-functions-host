// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
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
        [InlineData(ContentTypes.ApplicationOctetStream)]
        [InlineData(ContentTypes.TextPlain)]
        [InlineData(ContentTypes.ApplicationJson)]
        public async Task ConvertAsync_ReturnsExpectedResult(string contentType)
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
    }
}
