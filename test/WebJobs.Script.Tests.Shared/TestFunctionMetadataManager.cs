// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Abstractions.Description;
using Microsoft.Azure.WebJobs.Script.Workers.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public static class TestFunctionMetadataManager
    {
        public static FunctionMetadataManager GetFunctionMetadataManager(IOptions<ScriptJobHostOptions> jobHostOptions, IFunctionMetadataProvider functionMetadataProvider,
            IOptions<HttpWorkerOptions> httpOptions, ILoggerFactory loggerFactory)
        {
            return GetFunctionMetadataManager(jobHostOptions, functionMetadataProvider, new List<IFunctionProvider>(), httpOptions, loggerFactory);
        }

        public static FunctionMetadataManager GetFunctionMetadataManager(IOptions<ScriptJobHostOptions> jobHostOptions,
            IFunctionMetadataProvider functionMetadataProvider, IList<IFunctionProvider> functionProviders, IOptions<HttpWorkerOptions> httpOptions, ILoggerFactory loggerFactory)
        {
            var managerMock = new Mock<IScriptHostManager>();

            managerMock.As<IServiceProvider>().Setup(m => m.GetService(typeof(IEnumerable<IFunctionProvider>))).Returns(functionProviders);
            managerMock.As<IServiceProvider>().Setup(m => m.GetService(typeof(IOptions<ScriptJobHostOptions>))).Returns(jobHostOptions);
            managerMock.As<IServiceProvider>().Setup(m => m.GetService(typeof(IOptions<HttpWorkerOptions>))).Returns(httpOptions);
            managerMock.As<IServiceProvider>().Setup(m => m.GetService(typeof(ILoggerFactory))).Returns(loggerFactory);

            return new FunctionMetadataManager(jobHostOptions, functionMetadataProvider, functionProviders, httpOptions, managerMock.Object, loggerFactory);
        }
    }
}
