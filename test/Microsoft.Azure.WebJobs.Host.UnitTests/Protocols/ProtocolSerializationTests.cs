// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Protocols
{
    public class ProtocolSerializationTests
    {
        [Fact]
        public void ParameterDescriptor_ExtendedPropertiesRoundtrip()
        {
            MyCustomParameterDescriptor descriptor = new MyCustomParameterDescriptor
            {
                Name = "MyProperty",
                CustomProperty1 = "MyValue1",
                CustomProperty2 = true,
                CustomProperty3 = 123
            };

            StringWriter sw = new StringWriter();
            JsonSerialization.Serializer.Serialize(sw, descriptor);
            string json = sw.ToString();

            JsonTextReader reader = new JsonTextReader(new StringReader(json));
            ParameterDescriptor result = JsonSerialization.Serializer.Deserialize<ParameterDescriptor>(reader);

            // Verify extended properties were deserialized
            Assert.Equal(3, result.ExtendedProperties.Count);
            Assert.Equal(descriptor.CustomProperty1, (string)result.ExtendedProperties["CustomProperty1"]);
            Assert.Equal(descriptor.CustomProperty2, (bool)result.ExtendedProperties["CustomProperty2"]);
            Assert.Equal(descriptor.CustomProperty3, (int)result.ExtendedProperties["CustomProperty3"]);

            // Serialize the base ParameterDescriptor instance again
            sw = new StringWriter();
            JsonSerialization.Serializer.Serialize(sw, result);
            json = sw.ToString();

            // Deserialize and verify the properties roundtrip
            reader = new JsonTextReader(new StringReader(json));
            result = JsonSerialization.Serializer.Deserialize<ParameterDescriptor>(reader);

            // Verify extended properties were deserialized
            Assert.Equal(3, result.ExtendedProperties.Count);
            Assert.Equal(descriptor.CustomProperty1, (string)result.ExtendedProperties["CustomProperty1"]);
            Assert.Equal(descriptor.CustomProperty2, (bool)result.ExtendedProperties["CustomProperty2"]);
            Assert.Equal(descriptor.CustomProperty3, (int)result.ExtendedProperties["CustomProperty3"]);
        }
    }

    public class MyCustomParameterDescriptor : ParameterDescriptor
    {
        public string CustomProperty1 { get; set; }
        public bool CustomProperty2 { get; set; }
        public int CustomProperty3 { get; set; }
    }
}
