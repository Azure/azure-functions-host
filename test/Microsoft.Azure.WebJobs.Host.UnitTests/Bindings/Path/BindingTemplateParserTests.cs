// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Host.Bindings.Path;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Bindings.Path
{
    public class BindingTemplateParserTests
    {
        [Fact]
        public void ParseTemplate_IfValidInput_ReturnsTokens()
        {
            var tokens = BindingTemplateParser.ParseTemplate(@"{p1}-p2/{{2014}}/{d3}/folder/{name}.{ext}");

            Assert.NotNull(tokens);
            Assert.Equal(new List<string> { "p1", "-p2/", "{", "2014", "}", "/", "d3", "/folder/", "name", ".", "ext" },
                GetTokenValues(tokens));
        }

        [Fact]
        public void ParseTemplate_IfPathWithNoParameters_ReturnsLiteralTokensOnly()
        {
            var tokens = BindingTemplateParser.ParseTemplate("path-with-no-parameters");

            Assert.Equal(1, tokens.Count);
            Assert.Equal("path-with-no-parameters", tokens[0].AsLiteral);
        }

        [Fact]
        public void ParseTemplate_IfPathContainsParameters_ReturnsParameterTokens()
        {
            var tokens = BindingTemplateParser.ParseTemplate("{path}-with-{parameters}");

            Assert.Equal(new List<string> { "path", "parameters" }, GetParameterNames(tokens));
        }

        [Fact]
        public void ParseTemplate_IfMissingClosingBracket_ThrowsFormat()
        {
            ExceptionAssert.ThrowsFormat(
                () => BindingTemplateParser.ParseTemplate("malformed-{path"),
                "Invalid template 'malformed-{path'. Missing closing bracket at position 11.");
        }

        [Fact]
        public void ParseTemplate_IfMissingOpeningBracket_ThrowsFormat()
        {
            ExceptionAssert.ThrowsFormat(
                () => BindingTemplateParser.ParseTemplate("malformed}-path"),
                "Invalid template 'malformed}-path'. Missing opening bracket at position 10.");
        }

        [Fact]
        public void ParseTemplate_IfEmptyString_ReturnsEmptySequence()
        {
            var tokens = BindingTemplateParser.ParseTemplate(String.Empty);

            Assert.NotNull(tokens);
            Assert.Empty(tokens);
        }

        [Fact]
        public void ParseTemplate_IfNull_Throws()
        {
            ExceptionAssert.ThrowsArgumentNull(
                () => BindingTemplateParser.ParseTemplate(null),
                "input");
        }

        [Fact]
        public void ParseTemplate_IfMissingParam_ThrowsFormat()
        {
            ExceptionAssert.ThrowsFormat(
                () => BindingTemplateParser.ParseTemplate("{}-path"),
                "Invalid template '{}-path'. The parameter name at position 1 is empty.");
        }

        [Fact]
        public void ParseTemplate_IfMalformedParam_ThrowsFormat()
        {
            ExceptionAssert.ThrowsFormat(
                () => BindingTemplateParser.ParseTemplate("{malformed-param}-path"),
                "Invalid template '{malformed-param}-path'. The parameter name 'malformed-param' is invalid.");
        }

        [Fact]
        public void ParseTemplate_IfUnbalancedBraces_ThrowsFormat()
        {
            ExceptionAssert.ThrowsFormat(
                () => BindingTemplateParser.ParseTemplate("}{param}{-path"),
                "Invalid template '}{param}{-path'. Missing opening bracket at position 1.");
        }

        [Fact]
        public void ParseTemplate_IfTripleBrackets_RecognizesParameter()
        {
            var tokens = BindingTemplateParser.ParseTemplate("{{{parameter}}}");

            Assert.Equal(new List<string> { "{", "parameter", "}" }, GetTokenValues(tokens));
        }

        [Fact]
        public void ParseTemplate_IfNestedParam_ThrowsFormat()
        {
            ExceptionAssert.ThrowsFormat(
                () => BindingTemplateParser.ParseTemplate("{a{nested}param}"),
                "Invalid template '{a{nested}param}'. Missing closing bracket at position 1.");
        }

        private static IEnumerable<string> GetParameterNames(IEnumerable<BindingTemplateToken> tokens)
        {
            return from token in tokens
                   let name = token.ParameterName
                   where name != null
                   select name;
        }

        private static IEnumerable<string> GetTokenValues(IEnumerable<BindingTemplateToken> tokens)
        {
            return from token in tokens select GetValue(token);
        }

        private static string GetValue(BindingTemplateToken token)
        {
            var value = token.AsLiteral ?? token.ParameterName;
            return value;
        }
    }
}
