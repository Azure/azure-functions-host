// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Extensions
{
    public class ScriptLoggingBuilderExtensionsTests
    {
        [Fact]
        public void AddForwardingLogger_NullBuilder_Throws()
        {
            TestHelpers.Act(() => ScriptLoggingBuilderExtensions.AddForwardingLogger(null!))
                .Should().Throw<ArgumentNullException>()
                .WithParameterName("builder");
        }

        [Fact]
        public void AddForwardingLogger_AddsServices()
        {
            ServiceCollection services = new();
            services.AddLogging(b => b.AddForwardingLogger());

            services.Should().ContainSingle(
                s => s.IsKeyedService && s.KeyedImplementationType == typeof(ForwardingLoggerFactory))
                .Which.Should().Satisfy<ServiceDescriptor>(sd =>
                {
                    sd.ServiceType.Should().Be(typeof(ILoggerFactory));
                    sd.Lifetime.Should().Be(ServiceLifetime.Singleton);
                    sd.ServiceKey.Should().Be(ForwardingLogger.ServiceKey);
                });

            services.Should().ContainSingle(
                s => s.IsKeyedService && s.KeyedImplementationType == typeof(ForwardingLogger<>))
                .Which.Should().Satisfy<ServiceDescriptor>(sd =>
                {
                    sd.ServiceType.Should().Be(typeof(ILogger<>));
                    sd.Lifetime.Should().Be(ServiceLifetime.Singleton);
                    sd.ServiceKey.Should().Be(ForwardingLogger.ServiceKey);
                });
        }
    }
}
