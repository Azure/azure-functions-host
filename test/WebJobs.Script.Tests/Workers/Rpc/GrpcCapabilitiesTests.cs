// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf.Collections;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc
{
    public class GrpcCapabilitiesTests
    {
        private readonly TestLoggerProvider _loggerProvider;
        private readonly LoggerFactory _loggerFactory = new LoggerFactory();
        private readonly GrpcCapabilities _capabilities;

        private string testCapability1 = "TestCapability1";
        private string testCapability2 = "TestCapability2";

        public GrpcCapabilitiesTests()
        {
            _loggerProvider = new TestLoggerProvider();
            _loggerFactory.AddProvider(_loggerProvider);
            _capabilities = new GrpcCapabilities(_loggerFactory.CreateLogger<GrpcCapabilities>());
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

            _capabilities.UpdateCapabilities(addedCapabilities, GrpcCapabilitiesUpdateStrategy.Merge);

            Assert.Equal(value, _capabilities.GetCapabilityState(testCapability2));

            IDictionary<string, string> finalDict = new Dictionary<string, string> { { "testCapability2", value.ToString() } };

            var logs = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
            Assert.Collection(logs,
                p => Assert.Equal($"Updating capabilities using {GrpcCapabilitiesUpdateStrategy.Merge} strategy. Current: {new Dictionary<string, string>()} Incoming: {addedCapabilities}", p),
                p => Assert.Equal($"Updated capabilities: {finalDict}", p));
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

            _capabilities.UpdateCapabilities(addedCapabilities, GrpcCapabilitiesUpdateStrategy.Merge);

            Assert.Equal("true", _capabilities.GetCapabilityState(testCapability2));

            MapField<string, string> changedCapabilities = new MapField<string, string>
            {
                { testCapability2, "false" }
            };

            _capabilities.UpdateCapabilities(changedCapabilities, GrpcCapabilitiesUpdateStrategy.Merge);

            Assert.Equal("false", _capabilities.GetCapabilityState(testCapability2));

            IDictionary<string, string> firstUpdates = new Dictionary<string, string>()
            {
                { testCapability1, TestCapability.State1.ToString() },
                { "testCapability2", "true" }
            };

            IDictionary<string, string> finalCapabilities = new Dictionary<string, string>()
            {
                { testCapability1, TestCapability.State1.ToString() },
                { "testCapability2", "false" }
            };

            var logs = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
            Assert.Collection(logs,
                p => Assert.Equal($"Updating capabilities using {GrpcCapabilitiesUpdateStrategy.Merge} strategy. Current: {new Dictionary<string, string>()} Incoming: {addedCapabilities}", p),
                p => Assert.Equal($"Updated capabilities: {firstUpdates}", p),
                p => Assert.Equal($"Updating capabilities using {GrpcCapabilitiesUpdateStrategy.Merge} strategy. Current: {firstUpdates} Incoming: {changedCapabilities}", p),
                p => Assert.Equal($"Updated capabilities: {finalCapabilities}", p));
        }

        [Fact]
        public void Capability_Replace_Strategy_Handled_Correctly()
        {
            MapField<string, string> initialCapabilities = new MapField<string, string>
            {
                { testCapability2, "true" },
                { testCapability1, "false" }
            };

            _capabilities.UpdateCapabilities(initialCapabilities, GrpcCapabilitiesUpdateStrategy.Replace);

            IDictionary<string, string> initialDict = new Dictionary<string, string> { { "testCapability2", "false" } };

            MapField<string, string> changedCapabilities = new MapField<string, string>
            {
                { testCapability2, "false" }
            };

            _capabilities.UpdateCapabilities(changedCapabilities, GrpcCapabilitiesUpdateStrategy.Replace);

            IDictionary<string, string> finalDict = new Dictionary<string, string> { { "testCapability2", "false" } };

            var logs = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
            Assert.Collection(logs,
                p => Assert.Equal($"Updating capabilities using {GrpcCapabilitiesUpdateStrategy.Replace} strategy. Current: {new Dictionary<string, string>()} Incoming: {initialCapabilities}", p),
                p => Assert.Equal($"Updated capabilities: {initialDict}", p),
                p => Assert.Equal($"Updating capabilities using {GrpcCapabilitiesUpdateStrategy.Replace} strategy. Current: {new Dictionary<string, string>()} Incoming: {changedCapabilities}", p),
                p => Assert.Equal($"Updated capabilities: {finalDict}", p));
        }
    }
}
