// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Config;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    [Collection(DisableParallelizationCollection.Name)]
    public class ScriptSettingsManagerTests
    {
        [Fact]
        public void SettingsAreNotCached()
        {
            using (var variable = new TestScopedEnvironmentVariable(nameof(SettingsAreNotCached), "foo"))
            {
                Assert.Equal("foo", ScriptSettingsManager.Instance.GetSetting(nameof(SettingsAreNotCached)));

                Environment.SetEnvironmentVariable(nameof(SettingsAreNotCached), "bar");
                Assert.Equal("bar", ScriptSettingsManager.Instance.GetSetting(nameof(SettingsAreNotCached)));
            }
        }

        [Theory]
        [InlineData("Foo__Bar__Baz", "Foo__Bar__Baz")]
        [InlineData("Foo__Bar__Baz", "foo__bar__baz")]
        [InlineData("Foo__Bar__Baz", "Foo:Bar:Baz")]
        [InlineData("Foo__Bar__Baz", "foo:bar:baz")]
        [InlineData("Foo:Bar:Baz", "Foo:Bar:Baz")]
        [InlineData("Foo:Bar:Baz", "foo:bar:baz")]
        [InlineData("Foo_Bar_Baz", "Foo_Bar_Baz")]
        [InlineData("Foo_Bar_Baz", "foo_bar_baz")]
        [InlineData("FooBarBaz", "FooBarBaz")]
        [InlineData("FooBarBaz", "foobarbaz")]
        public void GetSetting_NormalizesKeys(string key, string lookup)
        {
            try
            {
                string value = Guid.NewGuid().ToString();
                Environment.SetEnvironmentVariable(key, value);

                string result = ScriptSettingsManager.Instance.GetSetting(lookup);
                Assert.Equal(value, result);
            }
            finally
            {
                Environment.SetEnvironmentVariable(key, null);
            }
        }
    }
}
