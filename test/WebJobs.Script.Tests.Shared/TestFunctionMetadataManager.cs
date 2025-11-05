// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Workers.Http;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    internal static class TestFunctionMetadataManager
    {
        public static FunctionMetadataManager GetFunctionMetadataManager(IOptions<ScriptJobHostOptions> jobHostOptions, IFunctionMetadataProvider functionMetadataProvider, ILoggerFactory loggerFactory, IOptionsMonitor<LanguageWorkerOptions> languageWorkerOptions)
        {
            return GetFunctionMetadataManager(jobHostOptions, functionMetadataProvider, new List<IFunctionProvider>(), loggerFactory, languageWorkerOptions);
        }

        public static FunctionMetadataManager GetFunctionMetadataManager(IOptions<ScriptJobHostOptions> jobHostOptions,
            IFunctionMetadataProvider functionMetadataProvider, IList<IFunctionProvider> functionProviders,
            ILoggerFactory loggerFactory, IOptionsMonitor<LanguageWorkerOptions> languageWorkerOptions)
        {
            var managerMock = new Mock<IScriptHostManager>();

            return GetFunctionMetadataManager(jobHostOptions, managerMock, functionMetadataProvider, functionProviders, loggerFactory, languageWorkerOptions);
        }

        public static FunctionMetadataManager GetFunctionMetadataManager(IOptions<ScriptJobHostOptions> jobHostOptions, Mock<IScriptHostManager> managerMock,
            IFunctionMetadataProvider functionMetadataProvider, IList<IFunctionProvider> functionProviders, ILoggerFactory loggerFactory, IOptionsMonitor<LanguageWorkerOptions> languageWorkerOptions)
        {
            var metadataOptions = new OptionsWrapper<FunctionMetadataOptions>(new FunctionMetadataOptions());

            managerMock.As<IServiceProvider>().Setup(m => m.GetService(typeof(IEnumerable<IFunctionProvider>))).Returns(functionProviders);
            managerMock.As<IServiceProvider>().Setup(m => m.GetService(typeof(IOptions<ScriptJobHostOptions>))).Returns(jobHostOptions);
            managerMock.As<IServiceProvider>().Setup(m => m.GetService(typeof(IOptionsMonitor<LanguageWorkerOptions>))).Returns(languageWorkerOptions);
            managerMock.As<IServiceProvider>().Setup(m => m.GetService(typeof(ILoggerFactory))).Returns(loggerFactory);
            managerMock.As<IServiceProvider>().Setup(m => m.GetService(typeof(IOptions<FunctionMetadataOptions>))).Returns(metadataOptions);

            var testData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "version", "2.0" }
            };

            var testActiveHostConfig = new ConfigurationBuilder()
                .AddInMemoryCollection(testData)
                .Build();

            managerMock.As<IServiceProvider>().Setup(m => m.GetService(typeof(IConfiguration))).Returns(testActiveHostConfig);

            var options = new ScriptApplicationHostOptions()
            {
                IsSelfHost = true,
                ScriptPath = TestHelpers.FunctionsTestDirectory,
                LogPath = TestHelpers.GetHostLogFileDirectory().FullName
            };
            var factory = new TestOptionsFactory<ScriptApplicationHostOptions>(options);
            var source = new TestChangeTokenSource<ScriptApplicationHostOptions>();
            var changeTokens = new[] { source };
            var optionsMonitor = new OptionsMonitor<ScriptApplicationHostOptions>(factory, changeTokens, factory);
            return new FunctionMetadataManager(jobHostOptions, functionMetadataProvider, managerMock.Object, loggerFactory, SystemEnvironment.Instance, languageWorkerOptions, metadataOptions);
        }

        public static FunctionMetadataManager GetFunctionMetadataManagerWithDefaultHostConfig(IOptions<ScriptJobHostOptions> jobHostOptions,
            IFunctionMetadataProvider functionMetadataProvider, IList<IFunctionProvider> functionProviders, IOptions<HttpWorkerOptions> httpOptions, ILoggerFactory loggerFactory, IOptionsMonitor<LanguageWorkerOptions> languageWorkerOptions)
        {
            var managerMock = new Mock<IScriptHostManager>();
            managerMock.As<IServiceProvider>().Setup(m => m.GetService(typeof(IEnumerable<IFunctionProvider>))).Returns(functionProviders);
            managerMock.As<IServiceProvider>().Setup(m => m.GetService(typeof(IOptions<ScriptJobHostOptions>))).Returns(jobHostOptions);
            managerMock.As<IServiceProvider>().Setup(m => m.GetService(typeof(IOptions<HttpWorkerOptions>))).Returns(httpOptions);
            managerMock.As<IServiceProvider>().Setup(m => m.GetService(typeof(IOptionsMonitor<LanguageWorkerOptions>))).Returns(languageWorkerOptions);
            managerMock.As<IServiceProvider>().Setup(m => m.GetService(typeof(ILoggerFactory))).Returns(loggerFactory);

            var testData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "AzureFunctionsJobHost:isDefaultHostConfig", "true" }
            };

            var testActiveHostConfig = new ConfigurationBuilder()
                .AddInMemoryCollection(testData)
                .Build();

            managerMock.As<IServiceProvider>().Setup(m => m.GetService(typeof(IConfiguration))).Returns(testActiveHostConfig);

            var options = new ScriptApplicationHostOptions()
            {
                IsSelfHost = true,
                ScriptPath = TestHelpers.FunctionsTestDirectory,
                LogPath = TestHelpers.GetHostLogFileDirectory().FullName
            };
            var factory = new TestOptionsFactory<ScriptApplicationHostOptions>(options);
            var source = new TestChangeTokenSource<ScriptApplicationHostOptions>();
            var changeTokens = new[] { source };
            var optionsMonitor = new OptionsMonitor<ScriptApplicationHostOptions>(factory, changeTokens, factory);
            var metadataOptions = new OptionsWrapper<FunctionMetadataOptions>(new FunctionMetadataOptions());
            return new FunctionMetadataManager(jobHostOptions, functionMetadataProvider, managerMock.Object, loggerFactory, SystemEnvironment.Instance, languageWorkerOptions, metadataOptions);
        }
    }
}