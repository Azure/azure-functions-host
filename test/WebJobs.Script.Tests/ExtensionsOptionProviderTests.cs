// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions.Common;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Description.DotNet.CSharp.Analyzers;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class ExtensionsOptionProviderTests
    {
        [Fact]
        public void DefaultValueBinding()
        {
            var (host, serviceCollection) = GetTestHost(new Dictionary<string, string>());

            // TODO: Consider generate KafkaExtensionConfigProvider dyamically.
            var kafkaBuilder = new TestWebJobsExtensionBuilder(
                serviceCollection,
                ExtensionInfo.FromInstance(new KafkaExtensionConfigProvider()));

            kafkaBuilder.BindOptions<KafkaOptions>();

            var extensionsOptionProvider = new ExtensionsOptionProvider(host.Services, kafkaBuilder.Services);
            var extensions = extensionsOptionProvider.GetExtensionOptions();
            var option = extensions.FirstOrDefault();
            // 64 is the default value
            Assert.Equal(64, option.Value["maxBatchSize"]);
        }

        [Fact]
        public void OverrideDefaultValue()
        {
            var optionsConfig = new Dictionary<string, string>()
            {
                { "kafka:maxBatchSize", "65" }
            };

            var (host, serviceCollection) = GetTestHost(optionsConfig);

            // TODO: Consider generate KafkaExtensionConfigProvider dyamically.
            var kafkaBuilder = new TestWebJobsExtensionBuilder(
                serviceCollection,
                ExtensionInfo.FromInstance(new KafkaExtensionConfigProvider()));

            kafkaBuilder.BindOptions<KafkaOptions>();

            var extensionsOptionProvider = new ExtensionsOptionProvider(host.Services, kafkaBuilder.Services);
            var extensions = extensionsOptionProvider.GetExtensionOptions();
            var option = extensions.FirstOrDefault();

            // 64 is the default value
            Assert.Equal("kafka", option.Key);
            Assert.Equal(65, option.Value["maxBatchSize"]);
        }

        public void IrregularSectionNameExtensions()
        { }

        private Tuple<IHost, IServiceCollection> GetTestHost(IDictionary<string, string> optionsConfig)
        {
            var hostBuilder = new HostBuilder();
            hostBuilder.ConfigureAppConfiguration(config =>
            {
                config.Add(new TestConfigurationSource(optionsConfig));
            });

            IServiceCollection serviceCollection = null;
            hostBuilder.ConfigureServices(services =>
            {
                serviceCollection = services;
            });

            var host = hostBuilder.Build();
            return Tuple.Create(host, serviceCollection);
        }

        public class TestConfigurationSource : IConfigurationSource
        {
            private IDictionary<string, string> _configuration;

            public TestConfigurationSource(IDictionary<string, string> configuration)
            {
                _configuration = configuration;
            }

            public IConfigurationProvider Build(IConfigurationBuilder builder)
            {
                return new TestExtensionsConfiguraionProvider(_configuration);
            }
        }

        public class TestExtensionsConfiguraionProvider : ConfigurationProvider
        {
            private readonly IDictionary<string, string> _configuration;

            public TestExtensionsConfiguraionProvider(IDictionary<string, string> configuration)
            {
                _configuration = configuration;
            }

            public override void Load()
            {
                foreach (var kv in _configuration)
                {
                    var key = $"{ConfigurationSectionNames.JobHost}{ConfigurationPath.KeyDelimiter}extensions{ConfigurationPath.KeyDelimiter}{kv.Key}";
                    Data[key] = kv.Value;
                }
            }
        }

        private class KafkaOptions : IOptionsFormatter
        {
            public int MaxBatchSize { get; set; } = 64;

            public string Format()
            {
                throw new NotImplementedException();
            }
        }

        private class KafkaExtensionConfigProvider : IExtensionConfigProvider
        {
            public void Initialize(ExtensionConfigContext context)
            {
                throw new NotImplementedException();
            }
        }

        private class EventHubOptions : IOptionsFormatter
        {
            public int BatchCheckpointFrequency { get; set; } = 100;

            public EventProcessorOptions EventProcessorOptions { get; set; } = new EventProcessorOptions();

            public InitialOffsetOptions InitialOffsetOptions { get; set; } = new InitialOffsetOptions();

            public string Format()
            {
                throw new NotImplementedException();
            }
        }

        public class EventProcessorOptions
        {
            public int MaxBatchSize { get; set; } = 10;

            public int PrefetchCount { get; set; } = 20;

            public Func<string, object> InitialOffsetProvider
            {
                get;
                set;
            }
            = x => x;

            public IWebProxy WebProxy
            {
                get;
                set;
            }
            = new Mock<IWebProxy>().Object;
        }

        public class InitialOffsetOptions
        {
            public string Type { get; set; } = string.Empty;

            public string EnqueuedTimeUTC { get; set; } = string.Empty;
        }

        private class EventHubExtensionConfigProvider : IExtensionConfigProvider
        {
            public void Initialize(ExtensionConfigContext context)
            {
                throw new NotImplementedException();
            }
        }

        private class BlobsExtensionConfigProvider : IExtensionConfigProvider
        {
            public void Initialize(ExtensionConfigContext context)
            {
                throw new NotImplementedException();
            }
        }

        private class BlobsOptions : IOptionsFormatter
        {
            public int MaxDegreeOfParallelism { get; set; }

            public string Format()
            {
                throw new NotImplementedException();
            }
        }

        // Storage Queue uses QueuesOptions on WebJobs SDK.
        private class QueuesExtensionConfigProvider : IExtensionConfigProvider
        {
            public void Initialize(ExtensionConfigContext context)
            {
                throw new NotImplementedException();
            }
        }

        private class ServiceBusExtensionConfigProvider : IExtensionConfigProvider
        {
            public void Initialize(ExtensionConfigContext context)
            {
                throw new NotImplementedException();
            }
        }

        private class ServiceBusOptions : IOptionsFormatter
        {
            public string Format()
            {
                throw new NotImplementedException();
            }
        }

        internal class TestWebJobsExtensionBuilder : IWebJobsExtensionBuilder
        {
            private readonly IServiceCollection _services;
            private readonly ExtensionInfo _extensionInfo;

            public TestWebJobsExtensionBuilder(IServiceCollection services, ExtensionInfo extentionInfo)
            {
                _services = services;
                _extensionInfo = extentionInfo;
            }

            public IServiceCollection Services => _services;

            public ExtensionInfo ExtensionInfo => _extensionInfo;
        }
    }
}
