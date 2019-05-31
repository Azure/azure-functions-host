// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Google.Protobuf.Collections;
using Microsoft.Azure.WebJobs.Script.Grpc.Capabilities;
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
        public static void Boolean_Capability_Handled_Correctly()
        {
            MapField<string, string> capabilities = new MapField<string, string>
            {
                { ExposedCapabilities.RawHttpBodyBytes, "true" }
            };

            Capabilities.UpdateCapabilities(capabilities);

            Assert.True(Capabilities.IsCapabilityEnabled(ExposedCapabilities.RawHttpBodyBytes));
        }

        [Fact]
        public static void Boolean_Capability_Change_Handled_Correctly()
        {
            MapField<string, string> capabilities = new MapField<string, string>
            {
                { ExposedCapabilities.RawHttpBodyBytes, "true" }
            };

            Capabilities.UpdateCapabilities(capabilities);

            Assert.True(Capabilities.IsCapabilityEnabled(ExposedCapabilities.RawHttpBodyBytes));

            MapField<string, string> changedCapabilities = new MapField<string, string>
            {
                { ExposedCapabilities.RawHttpBodyBytes, "false" }
            };

            Capabilities.UpdateCapabilities(changedCapabilities);

            Assert.False(Capabilities.IsCapabilityEnabled(ExposedCapabilities.RawHttpBodyBytes));
        }

        [Fact]
        public static void Multi_Value_Capability_Handled_Correctly()
        {
            MapField<string, string> capabilities = new MapField<string, string>
            {
                { testCapability, TestCapability.State1.ToString() }
            };

            Capabilities.UpdateCapabilities(capabilities);

            Assert.Equal(TestCapability.State1.ToString(), Capabilities.GetCapabilityState(testCapability));
        }

        [Fact]
        public static void Multi_Value_Capability_Change_Handled_Correctly()
        {
            MapField<string, string> capabilities = new MapField<string, string>
            {
                { testCapability, TestCapability.State1.ToString() }
            };

            Capabilities.UpdateCapabilities(capabilities);

            Assert.Equal(TestCapability.State1.ToString(), Capabilities.GetCapabilityState(testCapability));

            MapField<string, string> changedCapabilities = new MapField<string, string>
            {
                { testCapability, TestCapability.State3.ToString() }
            };

            Capabilities.UpdateCapabilities(changedCapabilities);

            Assert.Equal(TestCapability.State3.ToString(), Capabilities.GetCapabilityState(testCapability));
        }

        [Fact]
        public static void Multi_Value_And_Boolean_Capabilities_Handled_Correctly()
        {
            MapField<string, string> capabilities = new MapField<string, string>
            {
                { testCapability, TestCapability.State1.ToString() },
                { ExposedCapabilities.RawHttpBodyBytes, "true" }
            };

            Capabilities.UpdateCapabilities(capabilities);

            Assert.Equal(TestCapability.State1.ToString(), Capabilities.GetCapabilityState(testCapability));
            Assert.True(Capabilities.IsCapabilityEnabled(ExposedCapabilities.RawHttpBodyBytes));
        }
    }
}
