// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Azure.WebJobs.Description;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class HostsOptionsProviderTests
    {
        [Fact]
        public void OverwriteWithAppSettings_Success()
        {
            var settings = new Dictionary<string, string>
                {
                    { "AzureFunctionsJobHost:extensions:test:config1", "test1" },
                    { "AzureFunctionsJobHost:extensions:test:config2", "test2" }
                };
            var payload = GetHostOptionProviderPayload<TestExtensionConfigProvider, TestOptions>(settings);
            Assert.Equal(ReadFixture("TestBasicBindings.json"), payload);
        }

        [Fact]
        public void IrregularNamingConvention_Success()
        {
            var settings = new Dictionary<string, string>
            {
                { "AzureFunctionsJobHost:extensions:eventHubs:config1", "test1" },
                { "AzureFunctionsJobHost:extensions:eventHubs:config2", "test2" }
            };
            var payload = GetHostOptionProviderPayload<TestEventHubConfigProvider, EventHubOptions>(settings);
            Assert.Equal(ReadFixture("TestIrregularNamingBindings.json"), payload);
        }

        [Fact]
        public void InCompliant_Extension_Without_ExtensionsAttribute_Success()
        {
            var settings = new Dictionary<string, string>
                {
                    { "AzureFunctionsJobHost:extensions:testNoExtensionAttributeConfigProvider:config1", "test1" },
                    { "AzureFunctionsJobHost:extensions:testNoExtensionAttributeConfigProvider:config2", "test2" }
                };

            var payload = GetHostOptionProviderPayload<TestNoExtensionAttributeConfigProvider, TestOptions>(settings);
            Assert.Equal(ReadFixture("TestWihtoutExtensionsAttributeBindings.json"), payload);
        }

        [Fact]
        public void InCompliant_Extension_Without_IOptionFormatters_Ignored()
        {
            var settings = new Dictionary<string, string>
                {
                    { "AzureFunctionsJobHost:extensions:test:config1", "test1" },
                    { "AzureFunctionsJobHost:extensions:test:config2", "test2" }
                };
            var payload = GetHostOptionProviderPayload<TestExtensionConfigProvider, TestNoIOptionsFormatterOptions>(settings);
            Assert.Equal(ReadFixture("TestWihtoutIOptionsFormatter.json"), payload);
        }

        [Fact]
        public void Multiple_Extensions_Works_Success()
        {
            var settings = new Dictionary<string, string>
            {
                { "AzureWebJobsConfigurationSection", "AzureFunctionsJobHost" },
                { "AzureFunctionsJobHost:extensions:test:config1", "test1" },
                { "AzureFunctionsJobHost:extensions:test:config2", "test2" },
                { "AzureFunctionsJobHost:extensions:eventHubs:config1", "test0" },
                { "AzureFunctionsJobHost:extensions:eventHubs:config2", "test2" }
            };

            var hostBuilder = new HostBuilder()
            .ConfigureAppConfiguration(b =>
            {
                b.AddInMemoryCollection(settings);
            })
            .ConfigureDefaultTestHost(b =>
            {
                b.AddExtension<TestExtensionConfigProvider>()
                .BindOptions<TestOptions>();
                b.AddExtension<TestEventHubConfigProvider>()
                .BindOptions<EventHubOptions>();
            })
            .ConfigureServices((context, services) =>
            {
                services.AddSingleton<IHostOptionsProvider, HostOptionsProvider>();
            });

            var host = hostBuilder.Build();
            var provider = host.Services.GetService<IHostOptionsProvider>();
            var payload = provider.GetOptions();
            Assert.Equal(ReadFixture("TestMultipleBindings.json"), payload.ToString());
        }

        [Fact]
        public void Multiple_Option_Resigered_Works_Success()
        {
            var settings = new Dictionary<string, string>
            {
                { "AzureWebJobsConfigurationSection", "AzureFunctionsJobHost" },
                { "AzureFunctionsJobHost:extensions:test:config1", "test1" },
                { "AzureFunctionsJobHost:extensions:test:config2", "test2" }
            };

            var hostBuilder = new HostBuilder()
            .ConfigureAppConfiguration(b =>
            {
                b.AddInMemoryCollection(settings);
            })
            .ConfigureDefaultTestHost(b =>
            {
                // Bind Options called two times for the same Options.
                b.AddExtension<TestExtensionConfigProvider>()
                .BindOptions<TestOptions>()
                .BindOptions<TestOptions>();
            })
            .ConfigureServices((context, services) =>
            {
                services.AddSingleton<IHostOptionsProvider, HostOptionsProvider>();
            });

            var host = hostBuilder.Build();
            var provider = host.Services.GetService<IHostOptionsProvider>();
            var payload = provider.GetOptions();
            Assert.Equal(ReadFixture("TestBasicBindings.json"), payload.ToString());
        }

        [Fact]
        public void ConcurrencyOption_Success()
        {
            var settings = new Dictionary<string, string>
                {
                    { "AzureFunctionsJobHost:concurrency:dynamicConcurrencyEnabled", "true" },
                    { "AzureFunctionsJobHost:concurrency:maximumFunctionConcurrency", "20" },
                };

            var payload = GetHostOptionProviderPayload<TestExtensionConfigProvider, TestOptions>(settings);
            Assert.Equal(ReadFixture("TestBasicConcurrency.json"), payload);
        }

        [Fact]
        public void ConcurrencyOption_Default_Success()
        {
            var settings = new Dictionary<string, string>();
            var payload = GetHostOptionProviderPayload<TestExtensionConfigProvider, TestOptions>(settings);
            Assert.Equal(ReadFixture("TestDefaultConcurrency.json"), payload);
        }

        private string GetHostOptionProviderPayload<TExtension, TOptions>(Dictionary<string, string> settings) where TExtension : class, IExtensionConfigProvider where TOptions : class, new()
        {
            var host = SetupHostOptionProvider<TExtension, TOptions>(settings);
            var provider = host.Services.GetService<IHostOptionsProvider>();
            return provider.GetOptions().ToString();
        }

        private IHost SetupHostOptionProvider<TExtension, TOptions>(Dictionary<string, string> settings) where TExtension : class, IExtensionConfigProvider where TOptions : class, new()
        {
            settings["AzureWebJobsConfigurationSection"] = "AzureFunctionsJobHost";

            var hostBuilder = new HostBuilder()
            .ConfigureAppConfiguration(b =>
            {
                b.AddInMemoryCollection(settings);
            })
            .ConfigureDefaultTestHost(b =>
            {
                b.AddExtension<TExtension>()
                .BindOptions<TOptions>();
            })
            .ConfigureServices((context, services) =>
            {
                services.AddSingleton<IHostOptionsProvider, HostOptionsProvider>();
            });

            return hostBuilder.Build();
        }

        public static string ReadFixture(string name)
        {
            var path = Path.Combine("TestFixture", "HostOptionsProviderTests", name);
            return File.ReadAllText(path);
        }

        // class definition TestProvider
        [Extension("Test")]
        private class TestExtensionConfigProvider : IExtensionConfigProvider
        {
            public TestExtensionConfigProvider()
            {
            }

            public void Initialize(ExtensionConfigContext context)
            {
            }
        }

        private class TestOptions : IOptionsFormatter
        {
            public string Config1 { get; set; } = "default";

            public string Config2 { get; set; } = "default";

            public string Config3 { get; set; } = "default";

            public string Format()
            {
                return JObject.FromObject(this).ToString();
            }
        }

        // class definition TestEventHubs
        [Extension("EventHubs", configurationSection: "EventHubs")]
        private class TestEventHubConfigProvider : IExtensionConfigProvider
        {
            public TestEventHubConfigProvider()
            {
            }

            public void Initialize(ExtensionConfigContext context)
            {
            }
        }

        private class EventHubOptions : IOptionsFormatter
        {
            public string Config1 { get; set; } = "default";

            public string Config2 { get; set; } = "default";

            public string Config3 { get; set; } = "default";

            public string Format()
            {
                return JObject.FromObject(this).ToString();
            }
        }

        // Non compliant extension
        // No Extension Section
        private class TestNoExtensionAttributeConfigProvider : IExtensionConfigProvider
        {
            public TestNoExtensionAttributeConfigProvider()
            {
            }

            public void Initialize(ExtensionConfigContext context)
            {
            }
        }

        // No IOptionsFormatter
        private class TestNoIOptionsFormatterOptions
        {
            public string Config1 { get; set; } = "default";

            public string Config2 { get; set; } = "default";

            public string Config3 { get; set; } = "default";
        }
    }

    public static class TestExtensions
    {
        public static IHostBuilder ConfigureDefaultTestHost(this IHostBuilder builder, Action<IWebJobsBuilder> configureWebJobs, params Type[] type)
        {
            return builder.ConfigureWebJobs(configureWebJobs);
        }
    }
}
