// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Dashboard.Data;
using Xunit;

namespace Dashboard.UnitTests.Data
{
    public class ConnectionStringProviderTests
    {
        [Fact]
        public void GetPossibleConnectionStrings_ReturnsAllAppSettingsAndConnectionStrings()
        {
            Environment.SetEnvironmentVariable("TestSetting1", "Test Value 1");
            Environment.SetEnvironmentVariable("TestSetting2", "Test Value 2");

            var possibleConnections = ConnectionStringProvider.GetPossibleConnectionStrings();

            Assert.Equal(possibleConnections["TestSetting1"], "Test Value 1");
            Assert.Equal(possibleConnections["TestSetting2"], "Test Value 2");
            Assert.Equal(possibleConnections["TestConnection1"], "Test Connection 1");
            Assert.Equal(possibleConnections["TestConnection2"], "Test Connection 2");
        }
    }
}
