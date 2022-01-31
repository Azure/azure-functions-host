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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class SyncTriggerOptionProviderTests
    {
        /// <summary>
        /// Testing Default Value Fetching
        /// Test Cases Context
        /// KafkaOptions: General Extensions that Payload need for Arc scenario.
        /// DurableTaskOptions: Payload need for Arc and doesn't support IOptionFormatter
        /// </summary>
        [Theory]
        [InlineData(typeof(KafkaOptions), typeof(KafkaExtensionConfigProvider), "maxBatchSize", 64)]
        [InlineData(typeof(DurableTaskOptions), typeof(DurableTaskExtensionConfigProvider), "extendedSessionIdleTimeoutInSeconds", 30)]
        public void DefaultValueBinding(Type optionType, Type extensionConfigProviderType, string shortOptionKey, int expected)
        {
            var (host, serviceCollection) = GetTestHost(new Dictionary<string, string>());
            var extesionConfigProvider = Activator.CreateInstance(extensionConfigProviderType) as IExtensionConfigProvider;

            // TODO: Consider generate KafkaExtensionConfigProvider dyamically.
            var extensionBuilder = new TestWebJobsExtensionBuilder(
                serviceCollection,
                ExtensionInfo.FromInstance(extesionConfigProvider));

            // Execute kafkaBuilder.BindOptions<T>();
            MethodInfo genericBindOption = typeof(WebJobsExtensionBuilderExtensions).GetMethod("BindOptions", BindingFlags.Public | BindingFlags.Static);
            MethodInfo bindOption = genericBindOption.MakeGenericMethod(new Type[] { optionType });
            bindOption.Invoke(null, new object[] { extensionBuilder });

            var extensionsOptionProvider = new SyncTriggerOptionProvider(host.Services, extensionBuilder.Services);
            var extensions = extensionsOptionProvider.GetExtensionOptions();
            var option = extensions.FirstOrDefault();
            // 64 is the default value
            Assert.Equal(expected, option.Value.SelectToken(shortOptionKey));
        }

        /// <summary>
        /// Testing Default Value Fetching
        /// Test Cases Context
        /// KafkaOptions: General Extensions that Payload need for Arc scenario.
        /// QueuesOptions: IrregularNaming convention between host.json(queue) and QueuesOptions
        /// EventHubOptions: IrregularNaming convention between host.json(eventHubs) and EventHubOptions
        /// BlobsOptions: IrregularNaming converntion between host.json(blob) and BlobsOptions
        /// DurableTaskOptions: Payload need for Arc and doesn't support IOptionFormatter
        /// </summary>
        [Theory]
        [InlineData("kafka", "kafka:maxBatchSize", "maxBatchSize", "65", typeof(KafkaOptions), typeof(KafkaExtensionConfigProvider), 65)]
        [InlineData("queue", "queue:batchSize", "batchSize", "10", typeof(QueuesOptions), typeof(QueuesExtensionConfigProvider), 10)]
        [InlineData("eventHubs", "eventHubs:eventProcessorOptions:maxBatchSize", "eventProcessorOptions.maxBatchSize", "12", typeof(EventHubOptions), typeof(EventHubExtensionConfigProvider), 12)]
        [InlineData("blob", "blob:maxDegreeOfParallelism", "maxDegreeOfParallelism", "13", typeof(BlobsOptions), typeof(BlobsExtensionConfigProvider), 13)]
        [InlineData("durableTask", "durableTask:extendedSessionIdleTimeoutInSeconds", "extendedSessionIdleTimeoutInSeconds", "14", typeof(DurableTaskOptions), typeof(DurableTaskExtensionConfigProvider), 14)]
        public void OverwriteDefaultValue(string optionName, string fullOptionKey, string shortOptionKey, string optionValue, Type optionType, Type extensionConfigProviderType, int expectedValue)
        {
            var optionsConfig = new Dictionary<string, string>()
            {
                { fullOptionKey, optionValue }
            };

            var (host, serviceCollection) = GetTestHost(optionsConfig);

            var extesionConfigProvider = Activator.CreateInstance(extensionConfigProviderType) as IExtensionConfigProvider;
            // TODO: Consider generate KafkaExtensionConfigProvider dyamically.
            var extensionBuilder = new TestWebJobsExtensionBuilder(
                serviceCollection,
                ExtensionInfo.FromInstance(extesionConfigProvider));

            // Execute kafkaBuilder.BindOptions<T>();
            MethodInfo genericBindOption = typeof(WebJobsExtensionBuilderExtensions).GetMethod("BindOptions", BindingFlags.Public | BindingFlags.Static);
            MethodInfo bindOption = genericBindOption.MakeGenericMethod(new Type[] { optionType });
            bindOption.Invoke(null, new object[] { extensionBuilder });

            var extensionsOptionProvider = new SyncTriggerOptionProvider(host.Services, extensionBuilder.Services);
            var extensions = extensionsOptionProvider.GetExtensionOptions();
            var option = extensions.FirstOrDefault();

            // 64 is the default value
            Assert.Equal(optionName, option.Key);
            Assert.Equal(expectedValue, option.Value.SelectToken(shortOptionKey));
        }

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
                return JsonConvert.SerializeObject(this);
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
                var json = new JObject();
                json["BatchCheckpointFrequency"] = BatchCheckpointFrequency;
                var eventProcessorOptions = new JObject();
                eventProcessorOptions["MaxBatchSize"] = EventProcessorOptions.MaxBatchSize;
                eventProcessorOptions["PrefetchCount"] = EventProcessorOptions.PrefetchCount;
                json["EventProcessorOptions"] = eventProcessorOptions;
                json["InitialOffsetOptions"] = JObject.FromObject(InitialOffsetOptions);
                return json.ToString();
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
                return JsonConvert.SerializeObject(this);
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

        private class QueuesOptions : IOptionsFormatter
        {
            public int BatchSize { get; set; }

            public string Format()
            {
                return JsonConvert.SerializeObject(this);
            }
        }

        // Storage Queue uses QueuesOptions on WebJobs SDK.
        private class DurableTaskExtensionConfigProvider : IExtensionConfigProvider
        {
            public void Initialize(ExtensionConfigContext context)
            {
                throw new NotImplementedException();
            }
        }

        // DurableTask doesn't implement IOptionFormatter
        private class DurableTaskOptions
        {
            public int ExtendedSessionIdleTimeoutInSeconds { get; set; } = 30;
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
