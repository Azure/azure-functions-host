// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Google.Protobuf.Collections;
using Microsoft.Azure.WebJobs.Script.Grpc.Capabilities;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Rpc
{
    public class CapabilitiesTests
    {
        private static string testCapability = "TestCapability";

        private enum TestCapability
        {
            State1,
            State2,
            State3
        }

        [Fact]
        public static void Single_Capability_Handled_Correctly()
        {
            Capabilities capabilities = new Capabilities();
            MapField<string, string> addedCapabilities = new MapField<string, string>
            {
                { LanguageWorkerConstants.RawHttpBodyBytes, "true" }
            };

            capabilities.UpdateCapabilities(addedCapabilities);

            Assert.Equal("true", capabilities.GetCapabilityState(LanguageWorkerConstants.RawHttpBodyBytes));
        }

        [Fact]
        public static void Single_Capability_Change_Handled_Correctly()
        {
            Capabilities capabilities = new Capabilities();
            MapField<string, string> addedCapabilities = new MapField<string, string>
            {
                { LanguageWorkerConstants.RawHttpBodyBytes, "true" }
            };

            capabilities.UpdateCapabilities(addedCapabilities);

            Assert.Equal("true", capabilities.GetCapabilityState(LanguageWorkerConstants.RawHttpBodyBytes));

            MapField<string, string> changedCapabilities = new MapField<string, string>
            {
                { LanguageWorkerConstants.RawHttpBodyBytes, "false" }
            };

            capabilities.UpdateCapabilities(changedCapabilities);

            Assert.Equal("false", capabilities.GetCapabilityState(LanguageWorkerConstants.RawHttpBodyBytes));
        }

        [Fact]
        public static void Multi_Value_Capability_Handled_Correctly()
        {
            Capabilities capabilities = new Capabilities();
            MapField<string, string> addedCapabilities = new MapField<string, string>
            {
                { testCapability, TestCapability.State1.ToString() }
            };

            capabilities.UpdateCapabilities(addedCapabilities);

            Assert.Equal(TestCapability.State1.ToString(), capabilities.GetCapabilityState(testCapability));
        }

        [Fact]
        public static void Multi_Value_Capability_Change_Handled_Correctly()
        {
            Capabilities capabilities = new Capabilities();
            MapField<string, string> addedCapabilities = new MapField<string, string>
            {
                { testCapability, TestCapability.State1.ToString() }
            };

            capabilities.UpdateCapabilities(addedCapabilities);

            Assert.Equal(TestCapability.State1.ToString(), capabilities.GetCapabilityState(testCapability));

            MapField<string, string> changedCapabilities = new MapField<string, string>
            {
                { testCapability, TestCapability.State3.ToString() }
            };

            capabilities.UpdateCapabilities(changedCapabilities);

            Assert.Equal(TestCapability.State3.ToString(), capabilities.GetCapabilityState(testCapability));
        }

        [Fact]
        public static void Multi_Value_Capabilities_Handled_Correctly()
        {
            Capabilities capabilities = new Capabilities();
            MapField<string, string> addedCapabilities = new MapField<string, string>
            {
                { testCapability, TestCapability.State1.ToString() },
                { LanguageWorkerConstants.RawHttpBodyBytes, "true" }
            };

            capabilities.UpdateCapabilities(addedCapabilities);

            Assert.Equal(TestCapability.State1.ToString(), capabilities.GetCapabilityState(testCapability));
            Assert.Equal("true", capabilities.GetCapabilityState(LanguageWorkerConstants.RawHttpBodyBytes));
        }
    }
}
