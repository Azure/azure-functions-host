// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public class ForwardingLoggerAttributeTests
    {
        [Fact]
        public void Key_IsCorrect()
        {
            ForwardingLoggerAttribute attribute = new();

            attribute.Key.Should().BeOfType<string>()
                .Which.Should().NotBeNullOrWhiteSpace()
                .And.Be(ForwardingLogger.ServiceKey);
        }

        [Fact]
        public void Import_GetsService()
        {
            object nonKeyed = new();
            object keyed = new();

            ServiceCollection services = new();
            services.AddSingleton(nonKeyed);
            services.AddKeyedSingleton(ForwardingLogger.ServiceKey, keyed);
            services.AddSingleton<TestClass>();

            TestClass test = services.BuildServiceProvider().GetRequiredService<TestClass>();

            test.Instance.Should().BeSameAs(keyed);
        }

        private class TestClass([ForwardingLogger] object instance)
        {
            public object Instance => instance;
        }
    }
}
