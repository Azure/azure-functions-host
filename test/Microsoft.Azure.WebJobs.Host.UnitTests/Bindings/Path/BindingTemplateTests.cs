// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Azure.WebJobs.Host.Bindings.Path;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Xunit;
using Xunit.Extensions;
using Newtonsoft.Json.Linq;
using System.Globalization;

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

            string captureRegex = BindingTemplateToken.BuildCapturePattern(tokens);

            Assert.NotEmpty(captureRegex);
            Assert.Equal("^(?<p1>.*)-p2/\\{2014}/(?<d3>.*)/folder/(?<name>.*)\\.(?<ext>.*)$", captureRegex);
            Assert.NotNull(new Regex(captureRegex, RegexOptions.Compiled));
        }

        [Fact]
        public void BuildCapturePattern_Doesnt_Allow_Expressions()
        {
            // binding expressions are not allowed in a capture pattern. 
            // Captures just get top-level names. 
            string pattern = @"{p1.p2}";
            var tokens = BindingTemplateParser.GetTokens(pattern).ToList();

            ExceptionAssert.ThrowsInvalidOperation(() => BindingTemplateToken.BuildCapturePattern(tokens),
                "Capture expressions can't include dot operators");
        }

        [Fact]
        public void Bind_IfValidInput_ReturnsResolvedPath()
        {
            BindingTemplate template = BindingTemplate.FromString(@"{p1}-p2/{{2014}}/{d3}/folder/{name}.{ext}");

            var parameters = new Dictionary<string, object> {{ "p1", "container" }, { "d3", "path/to" }, 
                { "name", "file.1" }, { "ext", "txt" }};

            string resolvedText = template.Bind(parameters);

            Assert.NotEmpty(resolvedText);
            Assert.Equal("container-p2/{2014}/path/to/folder/file.1.txt", resolvedText);
        }

        [Fact]
        public void Bind_IfNonParameterizedPath_ReturnsResolvedPath()
        {
            BindingTemplate template = BindingTemplate.FromString("container");
            var parameters = new Dictionary<string, object> { { "name", "value" } };

            string result = template.Bind(parameters);

            Assert.Equal("container", result);
        }

        [Fact]
        public void Bind_IfParameterizedPath_ReturnsResolvedPath()
        {
            BindingTemplate template = BindingTemplate.FromString(@"container/{name}");
            var parameters = new Dictionary<string, object> { { "name", "value" } };

            string result = template.Bind(parameters);

            Assert.Equal(@"container/value", result);
        }

        [Fact]
        public void Bind_DotExpression()
        {
            BindingTemplate template = BindingTemplate.FromString(@"container/{a.b.c}");
            var parameters = new Dictionary<string, object> {
                { "a", new {
                    b = new {
                        c = 123
                    }
                }}
            };

            string result = template.Bind(parameters);

            Assert.Equal(@"container/123", result);
        }

        [Theory]
        [InlineData("container/{a.x-header}", "container/header", null)] // with dashes, like request
        [InlineData("container/{a.prop}", "container/bar", null)]
        [InlineData("container/{a.pRop}", "container/bar", null)] // casing 
        [InlineData("container/{a.missing}", null, "Error while accessing 'missing': property doesn't exist.")]
        [InlineData("a{null}b", "ab", null)]
        [InlineData("a{a.null}b", "ab", null)]
        public void Bind_DotExpression_With_JObject(string templateSource, string value, string error)
        {
            JObject jobj = new JObject();
            jobj["prop"] = "bar";
            jobj["x-header"] = "header";
            jobj["null"] = null;

            var parameters = new Dictionary<string, object> {
                { "a", jobj },
                { "null", null }
            };

            BindingTemplate template = BindingTemplate.FromString(templateSource);
            
            if (error != null)
            {
                ExceptionAssert.ThrowsInvalidOperation(() => template.Bind(parameters),
                error);
            }
            else
            {
                string result = template.Bind(parameters);
                Assert.Equal(value, result);
            }
        }

        [Fact]
        public void Bind_Dictionary()
        {
            var obj = new Dictionary<string, object>
            {
                { "p1", "v1" },
                { "p2", 123 }
            };

            var parameters = new Dictionary<string, object> {
                { "a", obj }                
            };

            var templateSource = "{a.p1}";
            BindingTemplate template = BindingTemplate.FromString(templateSource);
            string result = template.Bind(parameters);
            Assert.Equal("v1", result);

            templateSource = "{a.missing}";
            template = BindingTemplate.FromString(templateSource);
            ExceptionAssert.ThrowsInvalidOperation(() => template.Bind(parameters),
                "Error while accessing 'missing': property doesn't exist.");
        }

        // Accessing a missing property throws. 
        [Fact]
        public void Bind_DotExpression_Illegal()
        {
            BindingTemplate template = BindingTemplate.FromString(@"container/{a.missing}");
            var parameters = new Dictionary<string, object> {
                { "a", new {
                    b = new {
                        c = 123
                    }
                } }
            };

            ExceptionAssert.ThrowsInvalidOperation(() => template.Bind(parameters),
                "Error while accessing 'missing': property doesn't exist.");
        }

        [Fact]
        public void Bind_DotExpression_IsLateBound()
        {
            // Use same binding expression with different binding data inputs and it resolves dynamically.
            BindingTemplate template = BindingTemplate.FromString(@"container/{a.b.c}");
            var parameters1 = new Dictionary<string, object> {
                { "a", new {
                    b = new {
                        c = "first"
                    }
                }}
            };

            var parameters2 = new Dictionary<string, object> {
                { "a", new {
                    b = new {
                        c = 123,
                        d = 456
                    },
                    b2 = 789
                }}
            };
                        
            string result = template.Bind(parameters1);
            Assert.Equal(@"container/first", result);

            string result2 = template.Bind(parameters2);
            Assert.Equal(@"container/123", result2);
        }

        [Fact]
        public void Bind_IfMissingParameter_Throws()
        {
            BindingTemplate template = BindingTemplate.FromString(@"container/{missing}");
            var parameters = new Dictionary<string, object> { { "name", "value" } };

            // Act and Assert
            ExceptionAssert.ThrowsInvalidOperation(
                () => template.Bind(parameters),
                "No value for named parameter 'missing'.");
        }

        [Fact]
        public void FromString_CreatesCaseSensitiveTemplate()
        {
            BindingTemplate template = BindingTemplate.FromString(@"A/{B}/{c}");

            var parameters = new Dictionary<string, object>
            {
                { "B", "TestB" },
                { "c", "TestC" }
            };

            string result = template.Bind(parameters);
            Assert.Equal("A/TestB/TestC", result);

            parameters = new Dictionary<string, object>
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

            var parameters = new Dictionary<string, object>
            {
                { "B", "TestB" },
                { "c", "TestC" }
            };

            string result = template.Bind(parameters);
            Assert.Equal("A/TestB/TestC", result);

            parameters = new Dictionary<string, object>
            {
                { "b", "TestB" },
                { "C", "TestC" }
            };

            result = template.Bind(parameters);
            Assert.Equal("A/TestB/TestC", result);
        }


        [Fact]
        public void GuidFormats()
        {
            var g = Guid.NewGuid();
            var parameters = new Dictionary<string, object>
                {
                    { "g", g }
                };

            foreach (var format in new string[] { "N", "D", "B", "P", "X", "" })
            {

                BindingTemplate template = BindingTemplate.FromString(@"{g:" + format + "}");       

                string expected = g.ToString(format, CultureInfo.InvariantCulture);
                string result = template.Bind(parameters);

                Assert.Equal(expected, result);
            }
        }

        [Fact]
        public void DateTimeFormats()
        {
            var dt = DateTime.UtcNow;
            var parameters = new Dictionary<string, object>
                {
                    { "dt", dt }
                };


            var format = "YYYYMMdd";
            BindingTemplate template = BindingTemplate.FromString(@"{dt:" + format + "}");

            string expected = dt.ToString(format, CultureInfo.InvariantCulture);
            string result = template.Bind(parameters);

            Assert.Equal(expected, result);            
        }
    }
}
