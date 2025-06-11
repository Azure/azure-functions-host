// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Utils
{
    public sealed class TokenExtensionTests
    {
        [Theory]
        [InlineData("FeatureA,FeatureB,FeatureC", "FeatureB", ',', true)]
        [InlineData("FeatureA,FeatureB,FeatureC", "FeatureD", ',', false)]
        [InlineData("FeatureA,FeatureB,FeatureC", "featureb", ',', true)]
        [InlineData("FeatureA|FeatureB|FeatureC", "FeatureC", '|', true)]
        [InlineData("FeatureA,FeatureB,FeatureC", "FeatureA", ',', true)]
        [InlineData("FeatureA,FeatureB,FeatureC", "FeatureC", ',', true)]
        [InlineData(null, "FeatureA", ',', false)]
        [InlineData("", "FeatureA", ',', false)]
        [InlineData("", "", ',', false)]
        [InlineData("FeatureA,,FeatureB", "FeatureA", ',', true)]
        [InlineData("FeatureA,,FeatureB", "FeatureB", ',', true)]
        [InlineData(",FeatureA", "FeatureA", ',', true)]
        [InlineData("FeatureA,", "FeatureA", ',', true)]
        [InlineData("FeatureA,FeatureB,", "FeatureB", ',', true)]

        public void ContainsToken_WorksAsExpected(string delimited, string token, char separator, bool expected)
        {
            var result = delimited.ContainsToken(token, separator);

            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("FeatureA,FeatureB,FeatureC", "FeatureB", ',', true)]
        [InlineData("FeatureA,FeatureB,FeatureC", "FeatureD", ',', false)]
        [InlineData("FeatureA,FeatureB,FeatureC", "featureb", ',', true)]
        [InlineData("FeatureA|FeatureB|FeatureC", "FeatureC", '|', true)]
        [InlineData("FeatureA,FeatureB,FeatureC", "FeatureA", ',', true)]
        [InlineData("FeatureA,FeatureB,FeatureC", "FeatureC", ',', true)]
        [InlineData("FeatureA,,FeatureB", "FeatureA", ',', true)]
        [InlineData("FeatureA,,FeatureB", "FeatureB", ',', true)]
        [InlineData(",FeatureA", "FeatureA", ',', true)]
        [InlineData("FeatureA,", "FeatureA", ',', true)]
        [InlineData("FeatureA,FeatureB,", "FeatureB", ',', true)]
        public void ContainsToken_SpanOverload_WorksAsExpected(string delimited, string token, char separator, bool expected)
        {
            var result = delimited.AsSpan().ContainsToken(token.AsSpan(), separator);

            Assert.Equal(expected, result);
        }

        [Fact]
        public void ContainsToken_ThrowsArgumentException_WhenTokenContainsSeparator_StringOverload()
        {
            var ex = Assert.Throws<ArgumentException>(() =>
            {
                string source = "FeatureA,FeatureB";
                string invalidToken = "FeatureA,FeatureB";
                char separator = ',';

                source.ContainsToken(invalidToken, separator);
            });

            Assert.Contains("must not contain the separator", ex.Message);
        }

        [Fact]
        public void ContainsToken_ThrowsArgumentException_WhenTokenContainsSeparator_SpanOverload()
        {
            var ex = Assert.Throws<ArgumentException>(() =>
            {
                ReadOnlySpan<char> source = "FeatureA|FeatureB".AsSpan();
                ReadOnlySpan<char> invalidToken = "FeatureA|FeatureB".AsSpan();
                source.ContainsToken(invalidToken, '|');
            });

            Assert.Contains("must not contain the separator", ex.Message);
        }
    }
}
