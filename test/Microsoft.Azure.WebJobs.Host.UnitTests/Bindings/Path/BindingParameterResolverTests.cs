// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Bindings.Path;
using System.Linq;
using Xunit;
using System;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Bindings.Path
{
    public class BindingParameterResolverTests
    {
        [Theory]
        [InlineData("rand-guid", true)]
        [InlineData("RAND-GUID", true)]
        [InlineData("datetime", true)]
        [InlineData("foobar", false)]
        public void IsSystemParameter_ReturnsExpectedValue(string value, bool expected)
        {
            Assert.Equal(expected, BindingParameterResolver.IsSystemParameter(value));
        }

        [Fact]
        public void RandGuidResolver_ReturnsExpectedValue()
        {
            BindingParameterResolver resolver = null;
            BindingParameterResolver.TryGetResolver("rand-guid", out resolver);

            string resolvedValue = resolver.Resolve("rand-guid");
            Assert.Equal(36, resolvedValue.Length);
            Assert.Equal(4, resolvedValue.Count(p => p == '-'));

            resolvedValue = resolver.Resolve("rand-guid:");  // no format
            Assert.Equal(36, resolvedValue.Length);
            Assert.Equal(4, resolvedValue.Count(p => p == '-'));

            resolvedValue = resolver.Resolve("rand-guid:D");
            Assert.Equal(36, resolvedValue.Length);
            Assert.Equal(4, resolvedValue.Count(p => p == '-'));

            resolvedValue = resolver.Resolve("rand-guid:N");
            Assert.Equal(32, resolvedValue.Length);
            Assert.Equal(0, resolvedValue.Count(p => p == '-'));

            resolvedValue = resolver.Resolve("rand-guid:B");
            Assert.Equal(38, resolvedValue.Length);
            Assert.Equal(4, resolvedValue.Count(p => p == '-'));
            Assert.True(resolvedValue.StartsWith("{"));
            Assert.True(resolvedValue.EndsWith("}"));
        }

        [Fact]
        public void IncompatibleBindingExpression_ThrowsArgumentException()
        {
            BindingParameterResolver resolver = null;
            BindingParameterResolver.TryGetResolver("rand-guid", out resolver);

            var ex = Assert.Throws<ArgumentException>(() =>
            {
                resolver.Resolve("datetime:mm-dd-yyyy");
            });

            Assert.Equal("The value specified is not a 'rand-guid' binding parameter.\r\nParameter name: value", ex.Message);
        }

        [Fact]
        public void DateTimeResolver_ReturnsExpectedValue()
        {
            BindingParameterResolver resolver = null;
            BindingParameterResolver.TryGetResolver("datetime", out resolver);

            string resolvedValue = resolver.Resolve("datetime");

            resolvedValue = resolver.Resolve("datetime:G");
            Assert.NotNull(DateTime.Parse(resolvedValue));

            resolvedValue = resolver.Resolve("datetime:MM/yyyy");
            Assert.Equal(DateTime.UtcNow.ToString("MM/yyyy"), resolvedValue);

            resolvedValue = resolver.Resolve("datetime:yyyyMMdd");
            Assert.Equal(DateTime.UtcNow.ToString("yyyyMMdd"), resolvedValue);
        }
    }
}
