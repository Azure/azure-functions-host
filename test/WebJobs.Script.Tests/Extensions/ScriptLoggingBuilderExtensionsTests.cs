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

        [Theory]
        [InlineData("Microsoft.Azure.WebJobs.Host", LogLevel.Information, LogLevel.Information, true)]
        [InlineData("Microsoft.Azure.WebJobs.Host", LogLevel.Debug, LogLevel.Information, false)]
        [InlineData("Microsoft.Azure.WebJobs.Host", LogLevel.Trace, LogLevel.Trace, true)]
        [InlineData("Host.Startup", LogLevel.Information, LogLevel.Trace, true)]
        [InlineData("System.Net.Http", LogLevel.Information, LogLevel.Information, false)]
        public void Filter_RespectsMinLevelAndCategory(string category, LogLevel actualLevel, LogLevel minLevel, bool expected)
        {
            Assert.Equal(expected, ScriptLoggingBuilderExtensions.Filter(category, actualLevel, minLevel));
        }

        [Theory]
        [InlineData("Microsoft.Azure.WebJobs.Extensions.Storage.Common.Listeners.QueueListener", LogLevel.Debug)]
        [InlineData("Microsoft.Azure.WebJobs.Extensions.Storage.Common.Listeners.QueueListener", LogLevel.Trace)]
        [InlineData("Microsoft.Azure.WebJobs.EventHubs.EventHubProducerClientImpl", LogLevel.Debug)]
        [InlineData("Microsoft.Azure.WebJobs.EventHubs.EventHubProducerClientImpl", LogLevel.Trace)]
        [InlineData("Host.Executor", LogLevel.Debug)]
        [InlineData("Host.Executor", LogLevel.Trace)]
        public void Filter_SuppressedCategory_DebugAndTrace_ReturnsFalse(string category, LogLevel level)
        {
            Assert.False(ScriptLoggingBuilderExtensions.Filter(category, level, LogLevel.Trace));
        }

        [Theory]
        [InlineData("Microsoft.Azure.WebJobs.Extensions.Storage.Common.Listeners.QueueListener", LogLevel.Information)]
        [InlineData("Microsoft.Azure.WebJobs.Extensions.Storage.Common.Listeners.QueueListener", LogLevel.Warning)]
        [InlineData("Microsoft.Azure.WebJobs.EventHubs.EventHubProducerClientImpl", LogLevel.Information)]
        [InlineData("Microsoft.Azure.WebJobs.EventHubs.EventHubProducerClientImpl", LogLevel.Error)]
        [InlineData("Host.Executor", LogLevel.Information)]
        [InlineData("Host.Executor", LogLevel.Critical)]
        public void Filter_SuppressedCategory_InformationAndAbove_ReturnsTrue(string category, LogLevel level)
        {
            Assert.True(ScriptLoggingBuilderExtensions.Filter(category, level, LogLevel.Trace));
        }

        [Theory]
        [InlineData("Microsoft.Azure.WebJobs.Host", LogLevel.Debug)]
        [InlineData("Microsoft.Azure.WebJobs.Host", LogLevel.Trace)]
        [InlineData("Host.Startup", LogLevel.Debug)]
        [InlineData("Function.MyFunc", LogLevel.Trace)]
        public void Filter_NonSuppressedCategory_DebugAndTrace_ReturnsTrue(string category, LogLevel level)
        {
            Assert.True(ScriptLoggingBuilderExtensions.Filter(category, level, LogLevel.Trace));
        }
    }
}
