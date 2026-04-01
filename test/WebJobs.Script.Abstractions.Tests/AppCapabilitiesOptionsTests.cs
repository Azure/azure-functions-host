// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.AppCapabilities;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Abstractions.Tests
{
    public class AppCapabilitiesOptionsTests
    {
        [Fact]
        public void AppCapabilitiesOptions_Keys_IsCaseInsensitive()
        {
            var options = new AppCapabilitiesOptions();
            IDictionary<string, string> dict = options;

            dict.Add("MyCapability", "value1");

            Assert.True(dict.ContainsKey("MyCapability"));
            Assert.True(dict.ContainsKey("mycapability"));
            Assert.True(dict.ContainsKey("MYCAPABILITY"));
            Assert.True(dict.ContainsKey("myCapability"));
        }

        [Fact]
        public void AppCapabilitiesOptions_Indexer_IsCaseInsensitive()
        {
            var options = new AppCapabilitiesOptions();
            IDictionary<string, string> dict = options;

            dict["MyCapability"] = "value1";

            Assert.Equal("value1", dict["MyCapability"]);
            Assert.Equal("value1", dict["mycapability"]);
            Assert.Equal("value1", dict["MYCAPABILITY"]);
            Assert.Equal("value1", dict["myCapability"]);
        }

        [Fact]
        public void AppCapabilitiesOptions_TryGetValue_IsCaseInsensitive()
        {
            var options = new AppCapabilitiesOptions();
            IDictionary<string, string> dict = options;

            dict["TestCapability"] = "testValue";

            Assert.True(dict.TryGetValue("TestCapability", out var value1));
            Assert.Equal("testValue", value1);

            Assert.True(dict.TryGetValue("testcapability", out var value2));
            Assert.Equal("testValue", value2);

            Assert.True(dict.TryGetValue("TESTCAPABILITY", out var value3));
            Assert.Equal("testValue", value3);
        }

        [Fact]
        public void AppCapabilitiesOptions_AddDifferentCasing_OverwritesSameKey()
        {
            var options = new AppCapabilitiesOptions();
            IDictionary<string, string> dict = options;

            dict.Add("MyCapability", "value1");
            dict["mycapability"] = "value2";

            Assert.Single(dict);
            Assert.Equal("value2", dict["MyCapability"]);
        }

        [Fact]
        public void AppCapabilitiesOptions_Remove_IsCaseInsensitive()
        {
            var options = new AppCapabilitiesOptions();
            IDictionary<string, string> dict = options;

            dict.Add("MyCapability", "value1");
            Assert.True(dict.Remove("mycapability"));
            Assert.Empty(dict);
        }

        [Fact]
        public void AppCapabilitiesOptions_Contains_IsCaseInsensitive()
        {
            var options = new AppCapabilitiesOptions();
            ICollection<KeyValuePair<string, string>> collection = options;

            collection.Add(new KeyValuePair<string, string>("MyCapability", "value1"));

            Assert.True(collection.Contains(new KeyValuePair<string, string>("MyCapability", "value1")));
            Assert.True(collection.Contains(new KeyValuePair<string, string>("mycapability", "value1")));
            Assert.True(collection.Contains(new KeyValuePair<string, string>("MYCAPABILITY", "value1")));
        }

        [Fact]
        public void AppCapabilitiesOptions_Keys_ReturnsAllKeys()
        {
            var options = new AppCapabilitiesOptions();
            IDictionary<string, string> dict = options;

            dict.Add("capability1", "value1");
            dict.Add("capability2", "value2");

            Assert.Equal(2, dict.Keys.Count);
            Assert.Contains("capability1", dict.Keys);
            Assert.Contains("capability2", dict.Keys);
        }
    }
}