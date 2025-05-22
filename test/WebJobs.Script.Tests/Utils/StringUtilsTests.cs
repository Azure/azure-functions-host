// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Utils;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Utils
{
    public sealed class StringUtilsTests
    {
        [Theory]
        [InlineData("FeatureA,FeatureB,FeatureC", "FeatureB", ',', true)]
        [InlineData("FeatureA,FeatureB,FeatureC", "FeatureD", ',', false)]
        [InlineData("FeatureA,FeatureB,FeatureC", "featureb", ',', true)]
        [InlineData("FeatureA|FeatureB|FeatureC", "FeatureC", '|', true)]
        [InlineData("FeatureA,FeatureB,FeatureC", "FeatureA", ',', true)]
        [InlineData("FeatureA,FeatureB,FeatureC", "FeatureC", ',', true)]
        [InlineData("FeatureA,FeatureB,FeatureC", "FeatureA,FeatureB", ',', false)]
        [InlineData(null, "FeatureA", ',', false)]
        [InlineData("", "FeatureA", ',', false)]
        public void ContainsToken_LargeInputScenarios(string delimited, string token, char separator, bool expected)
        {
            var result = StringUtils.ContainsToken(delimited, token, separator);

            Assert.Equal(expected, result);
        }
    }
}
