// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
            var results = new JArray();
            var collectorMock = new Mock<IAsyncCollector<JObject>>(MockBehavior.Strict);
            collectorMock.Setup(p => p.AddAsync(It.IsAny<JObject>(), CancellationToken.None))
                .Callback<JObject, CancellationToken>((mockObject, mockToken) =>
                {
                    results.Add(mockObject);
                }).Returns(Task.CompletedTask);

            var binderMock = new Mock<Binder>(MockBehavior.Strict);
            var attributes = new Attribute[] { new QueueAttribute("test") };
            binderMock.Setup(p => p.BindAsync<IAsyncCollector<JObject>>(attributes, CancellationToken.None)).ReturnsAsync(collectorMock.Object);

            BindingContext bindingContext = new BindingContext
            {
                Attributes = attributes,
                Binder = binderMock.Object,
                Value = json
            };

            await FunctionBinding.BindAsyncCollectorAsync<JObject>(bindingContext);

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

            var result = FunctionBinding.ReadAsEnumerable(ms).Cast<JObject>().ToArray();

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

            var result = FunctionBinding.ReadAsEnumerable(ms).Cast<JObject>().ToArray();

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

            var collection = FunctionBinding.ReadAsEnumerable(ms).Cast<JValue>().ToArray();

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

            var collection = FunctionBinding.ReadAsEnumerable(ms).Cast<string>().ToArray();

            Assert.Equal(1, collection.Length);
            Assert.Equal("Value1", (string)collection[0]);
        }

        [Fact]
        public void ReadAsCollection_StringArray_WithBOM()
        {
            JArray values = new JArray();
            values.Add("Value1");
            values.Add("Value2");
            values.Add("Value3");

            // add the BOM character to the string
            var json = "\xFEFF" + values.ToString() + "\r\n";

            MemoryStream ms = new MemoryStream();
            StreamWriter writer = new StreamWriter(ms);
            writer.Write(json);
            writer.Flush();
            ms.Position = 0;

            var collection = FunctionBinding.ReadAsEnumerable(ms).Cast<JValue>().ToArray();

            Assert.Equal(3, collection.Length);
            Assert.Equal("Value1", (string)collection[0]);
            Assert.Equal("Value2", (string)collection[1]);
            Assert.Equal("Value3", (string)collection[2]);
        }

        [Fact]
        public async Task BindParameterBindingDataAsync()
        {
            string contentString = "hello world";
            ParameterBindingData bindingData = new("1.0.0", "AzureStorageBlob", BinaryData.FromString(contentString), "application/json");

            var binderMock = new Mock<Binder>(MockBehavior.Strict);
            var attributes = new Attribute[] { new BlobAttribute("test") };
            binderMock.Setup(p => p.BindAsync<ParameterBindingData>(attributes, CancellationToken.None)).ReturnsAsync(bindingData);

            BindingContext bindingContext = new BindingContext
            {
                Attributes = attributes,
                Binder = binderMock.Object
            };

            await FunctionBinding.BindParameterBindingDataAsync(bindingContext);

            Assert.Equal(bindingData, bindingContext.Value);
        }
    }
}
