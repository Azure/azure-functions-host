// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Workers.Http;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public static class TestFunctionMetadataManager
    {
        public static FunctionMetadataManager GetFunctionMetadataManager(IOptions<ScriptJobHostOptions> jobHostOptions, IFunctionMetadataProvider functionMetadataProvider,
            IOptions<HttpWorkerOptions> httpOptions, ILoggerFactory loggerFactory, IOptionsMonitor<LanguageWorkerOptions> languageWorkerOptions)
        {
            return GetFunctionMetadataManager(jobHostOptions, functionMetadataProvider, new List<IFunctionProvider>(), httpOptions, loggerFactory, languageWorkerOptions);
        }

        public static FunctionMetadataManager GetFunctionMetadataManager(IOptions<ScriptJobHostOptions> jobHostOptions,
            IFunctionMetadataProvider functionMetadataProvider, IList<IFunctionProvider> functionProviders, IOptions<HttpWorkerOptions> httpOptions,
            ILoggerFactory loggerFactory, IOptionsMonitor<LanguageWorkerOptions> languageWorkerOptions)
        {
            var managerMock = new Mock<IScriptHostManager>();

            return GetFunctionMetadataManager(jobHostOptions, managerMock, functionMetadataProvider, functionProviders, httpOptions, loggerFactory, languageWorkerOptions);
        }

        public static FunctionMetadataManager GetFunctionMetadataManager(IOptions<ScriptJobHostOptions> jobHostOptions, Mock<IScriptHostManager> managerMock,
            IFunctionMetadataProvider functionMetadataProvider, IList<IFunctionProvider> functionProviders, IOptions<HttpWorkerOptions> httpOptions, ILoggerFactory loggerFactory, IOptionsMonitor<LanguageWorkerOptions> languageWorkerOptions)
        {
            managerMock.As<IServiceProvider>().Setup(m => m.GetService(typeof(IEnumerable<IFunctionProvider>))).Returns(functionProviders);
            managerMock.As<IServiceProvider>().Setup(m => m.GetService(typeof(IOptions<ScriptJobHostOptions>))).Returns(jobHostOptions);
            managerMock.As<IServiceProvider>().Setup(m => m.GetService(typeof(IOptions<HttpWorkerOptions>))).Returns(httpOptions);
            managerMock.As<IServiceProvider>().Setup(m => m.GetService(typeof(IOptionsMonitor<LanguageWorkerOptions>))).Returns(languageWorkerOptions);
            managerMock.As<IServiceProvider>().Setup(m => m.GetService(typeof(ILoggerFactory))).Returns(loggerFactory);

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
            return new FunctionMetadataManager(jobHostOptions, functionMetadataProvider, httpOptions, managerMock.Object, loggerFactory, SystemEnvironment.Instance);
        }
    }
}
