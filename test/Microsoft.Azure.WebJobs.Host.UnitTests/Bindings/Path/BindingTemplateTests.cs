// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Azure.WebJobs.Host.Bindings.Path;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Xunit;
using Xunit.Extensions;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Bindings.Path
{
    public class BindingTemplateTests
    {
        [Fact]
        public void CreateBindingData_IfExtensionSpecialScenario_ReturnsNamedParams()
        {
            BindingTemplateSource template = BindingTemplateSource.FromString("input/{name}.csv");

            var namedParams = template.CreateBindingData("input/a.b.csv");

            Assert.Equal("a.b", namedParams["name"]);
        }

        [Theory]
        [InlineData("input/{name}.{extension}", "input/foo.txt", "foo")]
        [InlineData("input/foo/{name}.{extension}", "input/foo/foo.txt", "foo")]
        [InlineData("input/{name}/foo/{skip}.{extension}", "input/foo/foo/bar.txt", "foo")]
        [InlineData("input/{name}.{extension}/foo/{part}", "input/foo.txt/foo/part", "foo")]
        [InlineData("input/{name}/{extension}", "input/foo/txt", "foo")]
        [InlineData("input/{name}/foo/{skip}.{extension}", "input/name/foo/skip/bar.txt", "name")]
        [InlineData("input/foo/{name}.{extension}", "input/foo/skip/bar.txt", "skip/bar")]
        public void CreateBindingData_IfNameExtensionCombination_ReturnsNamedParams(
            string pattern, string actualPath, string expectedName)
        {
            BindingTemplateSource template = BindingTemplateSource.FromString(pattern);

            var namedParams = template.CreateBindingData(actualPath);

            Assert.NotNull(namedParams);
            Assert.Equal(expectedName, namedParams["name"]);
            Assert.Equal("txt", namedParams["extension"]);
        }
        
        [Fact]
        public void CreateBindingData_IfExtensionLongest_WorksMultiple()
        {
            BindingTemplateSource template = BindingTemplateSource.FromString("input/{name}.{extension}-/{other}");

            var namedParams = template.CreateBindingData("input/foo.bar.txt-/asd-/ijij");

            Assert.Equal("foo.bar", namedParams["name"]);
            Assert.Equal("txt-/asd", namedParams["extension"]);
            Assert.Equal("ijij", namedParams["other"]);
        }

        [Fact]
        public void CreateBindingData_IfExtensionLongest_WorksMultiCharacterSeparator()
        {
            BindingTemplateSource template = BindingTemplateSource.FromString("input/{name}.-/{extension}");

            var namedParams = template.CreateBindingData("input/foo.-/bar.txt");

            Assert.Equal("foo", namedParams["name"]);
            Assert.Equal("bar.txt", namedParams["extension"]);
        }

        [Fact]
        public void CreateBindingData_IfNoMatch_ReturnsNull()
        {
            BindingTemplateSource template = BindingTemplateSource.FromString("input/{name}.-/");

            var namedParams = template.CreateBindingData("input/foo.bar.-/txt");

            Assert.Null(namedParams);
        }

        [Fact]
        public void BuildCapturePattern_IfValidTemplate_ReturnsValidRegexp()
        {
            string pattern = @"{p1}-p2/{{2014}}/{d3}/folder/{name}.{ext}";
            var tokens = BindingTemplateParser.GetTokens(pattern).ToList();

            string captureRegex = BindingTemplateSource.BuildCapturePattern(tokens);

            Assert.NotEmpty(captureRegex);
            Assert.Equal("^(?<p1>.*)-p2/\\{2014}/(?<d3>.*)/folder/(?<name>.*)\\.(?<ext>.*)$", captureRegex);
            Assert.NotNull(new Regex(captureRegex, RegexOptions.Compiled));
        }

        [Fact]
        public void Bind_IfValidInput_ReturnsResolvedPath()
        {
            BindingTemplate template = BindingTemplate.FromString(@"{p1}-p2/{{2014}}/{d3}/folder/{name}.{ext}");

            var parameters = new Dictionary<string, string> {{ "p1", "container" }, { "d3", "path/to" }, 
                { "name", "file.1" }, { "ext", "txt" }};

            string resolvedText = template.Bind(parameters);

            Assert.NotEmpty(resolvedText);
            Assert.Equal("container-p2/{2014}/path/to/folder/file.1.txt", resolvedText);
        }

        [Fact]
        public void Bind_IfNonParameterizedPath_ReturnsResolvedPath()
        {
            BindingTemplate template = BindingTemplate.FromString("container");
            var parameters = new Dictionary<string, string> { { "name", "value" } };

            string result = template.Bind(parameters);

            Assert.Equal("container", result);
        }

        [Fact]
        public void Bind_IfParameterizedPath_ReturnsResolvedPath()
        {
            BindingTemplate template = BindingTemplate.FromString(@"container/{name}");
            var parameters = new Dictionary<string, string> { { "name", "value" } };

            string result = template.Bind(parameters);

            Assert.Equal(@"container/value", result);
        }

        [Fact]
        public void Bind_IfMissingParameter_Throws()
        {
            BindingTemplate template = BindingTemplate.FromString(@"container/{missing}");
            var parameters = new Dictionary<string, string> { { "name", "value" } };

            // Act and Assert
            ExceptionAssert.ThrowsInvalidOperation(
                () => template.Bind(parameters),
                "No value for named parameter 'missing'.");
        }

        [Fact]
        public void FromString_CreatesCaseSensitiveTemplate()
        {
            BindingTemplate template = BindingTemplate.FromString(@"A/{B}/{c}");

            var parameters = new Dictionary<string, string>
            {
                { "B", "TestB" },
                { "c", "TestC" }
            };

            string result = template.Bind(parameters);
            Assert.Equal("A/TestB/TestC", result);

            parameters = new Dictionary<string, string>
            {
                { "b", "TestB" },
                { "c", "TestC" }
            };

            ExceptionAssert.ThrowsInvalidOperation(
                () => template.Bind(parameters),
                "No value for named parameter 'B'.");
        }

        [Fact]
        public void FromString_IgnoreCase_CreatesCaseInsensitiveTemplate()
        {
            BindingTemplate template = BindingTemplate.FromString(@"A/{B}/{c}", ignoreCase: true);

            var parameters = new Dictionary<string, string>
            {
                { "B", "TestB" },
                { "c", "TestC" }
            };

            string result = template.Bind(parameters);
            Assert.Equal("A/TestB/TestC", result);

            parameters = new Dictionary<string, string>
            {
                { "b", "TestB" },
                { "C", "TestC" }
            };

            result = template.Bind(parameters);
            Assert.Equal("A/TestB/TestC", result);
        }
    }
}
