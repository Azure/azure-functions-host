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

            BoolUtility.TryReadAsBool(properties, "MyValueA", out bool resultValueA);
            BoolUtility.TryReadAsBool(properties, "MyValueB", out bool resultValueB);
            BoolUtility.TryReadAsBool(properties, "MyValueC", out bool resultValueC);
            BoolUtility.TryReadAsBool(properties, "MyValueD", out bool resultValueD);

            Assert.True(resultValueA);
            Assert.False(resultValueB);
            Assert.True(resultValueC);
            Assert.False(resultValueD);
        }

        [Fact]
        public void TryReadAsBool_InvalidValue_ReturnsFalse()
        {
            var properties = new Dictionary<string, object>();
            properties.Add("MyValueA", "blah");

            BoolUtility.TryReadAsBool(properties, "MyValueA", out bool resultValueA);

            Assert.False(resultValueA);
        }

        [Fact]
        public void TryReadAsBool_NullValue_ReturnsFalse()
        {
            var properties = new Dictionary<string, object>();
            properties.Add("MyValueA", null);

            BoolUtility.TryReadAsBool(properties, "MyValueA", out bool resultValueA);

            Assert.False(resultValueA);
        }
    }
}