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

            options.Add("MyCapability", "value1");

            Assert.True(options.ContainsKey("MyCapability"));
            Assert.True(options.ContainsKey("mycapability"));
            Assert.True(options.ContainsKey("MYCAPABILITY"));
            Assert.True(options.ContainsKey("myCapability"));
        }

        [Fact]
        public void AppCapabilitiesOptions_Indexer_IsCaseInsensitive()
        {
            var options = new AppCapabilitiesOptions();

            options["MyCapability"] = "value1";

            Assert.Equal("value1", options["MyCapability"]);
            Assert.Equal("value1", options["mycapability"]);
            Assert.Equal("value1", options["MYCAPABILITY"]);
            Assert.Equal("value1", options["myCapability"]);
        }

        [Fact]
        public void AppCapabilitiesOptions_TryGetValue_IsCaseInsensitive()
        {
            var options = new AppCapabilitiesOptions();

            options["TestCapability"] = "testValue";

            Assert.True(options.TryGetValue("TestCapability", out var value1));
            Assert.Equal("testValue", value1);

            Assert.True(options.TryGetValue("testcapability", out var value2));
            Assert.Equal("testValue", value2);

            Assert.True(options.TryGetValue("TESTCAPABILITY", out var value3));
            Assert.Equal("testValue", value3);
        }

        [Fact]
        public void AppCapabilitiesOptions_AddDifferentCasing_OverwritesSameKey()
        {
            var options = new AppCapabilitiesOptions();

            options.Add("MyCapability", "value1");
            options["mycapability"] = "value2";

            Assert.Single(options);
            Assert.Equal("value2", options["MyCapability"]);
        }

        [Fact]
        public void AppCapabilitiesOptions_Remove_IsCaseInsensitive()
        {
            var options = new AppCapabilitiesOptions();

            options.Add("MyCapability", "value1");
            Assert.True(options.Remove("mycapability"));
            Assert.Empty(options);
        }

        [Fact]
        public void AppCapabilitiesOptions_Contains_IsCaseInsensitive()
        {
            var options = new AppCapabilitiesOptions();

            options.Add(new KeyValuePair<string, string>("MyCapability", "value1"));

            Assert.True(options.Contains(new KeyValuePair<string, string>("MyCapability", "value1")));
            Assert.True(options.Contains(new KeyValuePair<string, string>("mycapability", "value1")));
            Assert.True(options.Contains(new KeyValuePair<string, string>("MYCAPABILITY", "value1")));
        }

        [Fact]
        public void AppCapabilitiesOptions_Keys_ReturnsAllKeys()
        {
            var options = new AppCapabilitiesOptions();

            options.Add("capability1", "value1");
            options.Add("capability2", "value2");

            Assert.Equal(2, options.Keys.Count);
            Assert.Contains("capability1", options.Keys);
            Assert.Contains("capability2", options.Keys);
        }
    }
}
