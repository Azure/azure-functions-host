// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host.Bindings.Runtime;
using Microsoft.Azure.WebJobs.Script.Binding;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class FunctionBindingTests
    {
        [Fact]
        public async Task BindAsyncCollectorAsync_JObjectCollection()
        {
            JArray values = new JArray();
            for (int i = 1; i <= 3; i++)
            {
                JObject jsonObject = new JObject
                {
                    { "prop1", "value1" },
                    { "prop2", true },
                    { "prop3", 123 }
                };
                values.Add(jsonObject);
            }

            string json = values.ToString();
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            MemoryStream ms = new MemoryStream(bytes);

            var results = new JArray();
            var collectorMock = new Mock<IAsyncCollector<JObject>>(MockBehavior.Strict);
            collectorMock.Setup(p => p.AddAsync(It.IsAny<JObject>(), CancellationToken.None))
                .Callback<JObject, CancellationToken>((mockObject, mockToken) =>
                {
                    results.Add(mockObject);
                }).Returns(Task.CompletedTask);

            var binderMock = new Mock<IBinderEx>(MockBehavior.Strict);
            QueueAttribute attribute = new QueueAttribute("test");
            RuntimeBindingContext context = new RuntimeBindingContext(attribute);
            binderMock.Setup(p => p.BindAsync<IAsyncCollector<JObject>>(context, CancellationToken.None)).ReturnsAsync(collectorMock.Object);

            await FunctionBinding.BindAsyncCollectorAsync<JObject>(ms, binderMock.Object, context);

            Assert.Equal(3, results.Count);
            for (int i = 0; i < 3; i++)
            {
                JObject jsonObject = (JObject)results[i];
                Assert.Equal("value1", (string)jsonObject["prop1"]);
                Assert.Equal(true, (bool)jsonObject["prop2"]);
                Assert.Equal(123, (int)jsonObject["prop3"]);
            }
        }

        [Fact]
        public void ReadAsCollection_ObjectArray()
        {
            JArray values = new JArray();
            for (int i = 1; i <= 3; i++)
            {
                JObject jsonObject = new JObject
                {
                    { "prop1", "value1" },
                    { "prop2", true },
                    { "prop3", 123 }
                };
                values.Add(jsonObject);
            }

            string json = values.ToString();
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            MemoryStream ms = new MemoryStream(bytes);

            var result = FunctionBinding.ReadAsCollection(ms).ToArray();

            Assert.Equal(3, result.Length);
            for (int i = 0; i < 3; i++)
            {
                JObject jsonObject = (JObject)result[i];
                Assert.Equal("value1", (string)jsonObject["prop1"]);
                Assert.Equal(true, (bool)jsonObject["prop2"]);
                Assert.Equal(123, (int)jsonObject["prop3"]);
            }
        }

        [Fact]
        public void ReadAsCollection_ObjectSingleton()
        {
            JObject jsonObject = new JObject
            {
                { "prop1", "value1" },
                { "prop2", true },
                { "prop3", 123 }
            };

            string json = jsonObject.ToString();
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            MemoryStream ms = new MemoryStream(bytes);

            var result = FunctionBinding.ReadAsCollection(ms).ToArray();

            Assert.Equal(1, result.Length);
            jsonObject = (JObject)result[0];
            Assert.Equal("value1", (string)jsonObject["prop1"]);
            Assert.Equal(true, (bool)jsonObject["prop2"]);
            Assert.Equal(123, (int)jsonObject["prop3"]);
        }

        [Fact]
        public void ReadAsCollection_StringArray()
        {
            JArray values = new JArray();
            values.Add("Value1");
            values.Add("Value2");
            values.Add("Value3");

            string json = values.ToString();
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            MemoryStream ms = new MemoryStream(bytes);

            var collection = FunctionBinding.ReadAsCollection(ms).ToArray();

            Assert.Equal(3, collection.Length);
            Assert.Equal("Value1", (string)collection[0]);
            Assert.Equal("Value2", (string)collection[1]);
            Assert.Equal("Value3", (string)collection[2]);
        }

        [Fact]
        public void ReadAsCollection_StringSingleton()
        {
            byte[] bytes = Encoding.UTF8.GetBytes("Value1");
            MemoryStream ms = new MemoryStream(bytes);

            var collection = FunctionBinding.ReadAsCollection(ms).ToArray();

            Assert.Equal(1, collection.Length);
            Assert.Equal("Value1", (string)collection[0]);
        }
    }
}
