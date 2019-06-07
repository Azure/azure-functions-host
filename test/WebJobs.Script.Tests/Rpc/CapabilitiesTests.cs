// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using Google.Protobuf.Collections;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Rpc
{
    public class CapabilitiesTests
    {
        private readonly TestLoggerProvider _loggerProvider;
        private readonly LoggerFactory _loggerFactory = new LoggerFactory();
        private readonly Capabilities _capabilities;

        private string testCapability1 = "TestCapability1";
        private string testCapability2 = "TestCapability2";

        public CapabilitiesTests()
        {
            _loggerProvider = new TestLoggerProvider();
            _loggerFactory.AddProvider(_loggerProvider);
            _capabilities = new Capabilities(_loggerFactory.CreateLogger<Capabilities>());
        }

        private enum TestCapability
        {
            State1,
            State2,
            State3
        }

        [Theory]
        [InlineData("")]
        [InlineData("true")]
        [InlineData("randomValue")]
        public void Capability_Handled_Correctly(string value)
        {
            MapField<string, string> addedCapabilities = new MapField<string, string>
            {
                { testCapability2, value }
            };

            _capabilities.UpdateCapabilities(addedCapabilities);

            Assert.Equal(value, _capabilities.GetCapabilityState(testCapability2));

            var logs = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
            Assert.Collection(logs,
                p => Assert.Equal($"Requested capabilities: {addedCapabilities.ToString()}", p));
        }

        [Fact]
        public void Null_Capability_Value_Throws_Error()
        {
            MapField<string, string> addedCapabilities = new MapField<string, string>();

            Assert.Throws<ArgumentNullException>(() => addedCapabilities.Add(testCapability2, null));
        }

        [Fact]
        public void Single_Capability_Change_Among_Multiple_Handled_Correctly()
        {
            MapField<string, string> addedCapabilities = new MapField<string, string>
            {
                { testCapability1, TestCapability.State1.ToString() },
                { testCapability2, "true" }
            };

            _capabilities.UpdateCapabilities(addedCapabilities);

            Assert.Equal("true", _capabilities.GetCapabilityState(testCapability2));

            MapField<string, string> changedCapabilities = new MapField<string, string>
            {
                { testCapability2, "false" }
            };

            _capabilities.UpdateCapabilities(changedCapabilities);

            Assert.Equal("false", _capabilities.GetCapabilityState(testCapability2));

            var logs = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
            Assert.Collection(logs,
                p => Assert.Equal($"Requested capabilities: {addedCapabilities.ToString()}", p),
                p => Assert.Equal($"Requested capabilities: {changedCapabilities.ToString()}", p));
        }

        [Fact]
        public void No_Error_For_Null_Capabilities()
        {
            MapField<string, string> addedCapabilities = null;

            _capabilities.UpdateCapabilities(addedCapabilities);
        }
    }
}
