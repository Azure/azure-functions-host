// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Extensions
{
    public class BoolUtilityTests
    {
        [Fact]
        public void TryReadAsBool_ReturnsExpectedBoolValue()
        {
            var properties = new Dictionary<string, object>();
            properties.Add("MyValueA", "True");
            properties.Add("MyValueB", "False");
            properties.Add("MyValueC", true);
            properties.Add("MyValueD", false);

            Assert.True(BoolUtility.TryReadAsBool(properties, "MyValueA"));
            Assert.False(BoolUtility.TryReadAsBool(properties, "MyValueB"));
            Assert.True(BoolUtility.TryReadAsBool(properties, "MyValueC"));
            Assert.False(BoolUtility.TryReadAsBool(properties, "MyValueD"));
        }

        [Fact]
        public void TryReadAsBool_InvalidValue_ReturnsFalse()
        {
            var properties = new Dictionary<string, object>();
            properties.Add("MyValueA", "blah");

            Assert.False(BoolUtility.TryReadAsBool(properties, "MyValueA"));
        }

        [Fact]
        public void TryReadAsBool_NullValue_ReturnsFalse()
        {
            var properties = new Dictionary<string, object>();
            properties.Add("MyValueA", null);

            Assert.False(BoolUtility.TryReadAsBool(properties, "MyValueA"));
        }
    }
}