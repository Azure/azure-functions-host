// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.Jobs.Host.Bindings;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.UnitTests.Bindings
{
    public class BindingDataPathTests
    {
        [Fact]
        public void ExtensionsSpecialScenario()
        {
            var namedParams = BindingDataPath.CreateBindingData("input/{name}.csv", "input/a.b.csv");

            Assert.Equal("a.b", namedParams["name"]);
        }

        [Fact]
        public void ExtensionsLongestWorks()
        {
            var namedParams = BindingDataPath.CreateBindingData("input/{name}.{extension}", "input/foo.bar.txt");

            Assert.Equal("foo.bar", namedParams["name"]);
            Assert.Equal("txt", namedParams["extension"]);
        }

        [Fact]
        public void ExtensionsLongestWorksMultiple()
        {
            var namedParams = BindingDataPath.CreateBindingData("input/{name}.{extension}-/{other}", "input/foo.bar.txt-/asd-/ijij");

            Assert.Equal("foo.bar", namedParams["name"]);
            Assert.Equal("txt-/asd", namedParams["extension"]);
            Assert.Equal("ijij", namedParams["other"]);
        }

        [Fact]
        public void ExtensionsLongestWorksMultiCharacterSeparator()
        {
            var namedParams = BindingDataPath.CreateBindingData("input/{name}.-/{extension}", "input/foo.-/bar.txt");

            Assert.Equal("foo", namedParams["name"]);
            Assert.Equal("bar.txt", namedParams["extension"]);
        }

        [Fact]
        public void ExtensionsWorks()
        {
            var namedParams = BindingDataPath.CreateBindingData("input/{name}.{extension}", "input/foo.txt");

            Assert.Equal("foo", namedParams["name"]);
            Assert.Equal("txt", namedParams["extension"]);
        }

        [Fact]
        public void ExtensionsReturnsNullIfNoMatch()
        {
            var namedParams = BindingDataPath.CreateBindingData("input/{name}.-/", "input/foo.bar.-/txt");

            Assert.Null(namedParams);
        }
    }
}
