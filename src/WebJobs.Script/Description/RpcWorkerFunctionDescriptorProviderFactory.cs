// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Http;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Description;

internal class RpcWorkerFunctionDescriptorProviderFactory : IWorkerFunctionDescriptorProviderFactory
{
    private readonly IFunctionInvocationDispatcher _dispatcher;
    private readonly IApplicationLifetime _applicationLifetime;
    private readonly HttpWorkerOptions _httpWorkerOptions;
    private readonly ScriptJobHostOptions _scriptHostOptions;
    private readonly IOptionsMonitor<LanguageWorkerOptions> _languageWorkerOptionsMonitor;
    private readonly ILoggerFactory _loggerFactory;

    public RpcWorkerFunctionDescriptorProviderFactory(IFunctionInvocationDispatcherFactory dispatcherFactory, IApplicationLifetime applicationLifetime, IOptions<ScriptJobHostOptions> scriptHostOptions,
                    IOptions<HttpWorkerOptions> httpWorkerOptions, IOptionsMonitor<LanguageWorkerOptions> languageWorkerOptionsMonitor, ILoggerFactory loggerFactory)
    {
        _dispatcher = dispatcherFactory.GetFunctionDispatcher();
        _applicationLifetime = applicationLifetime;
        _httpWorkerOptions = httpWorkerOptions.Value;
        _scriptHostOptions = scriptHostOptions.Value;
        _languageWorkerOptionsMonitor = languageWorkerOptionsMonitor;
        _loggerFactory = loggerFactory;
    }

    public FunctionDescriptorProvider CreateHttpDescriptorProvider(ScriptHost host, ICollection<IScriptBindingProvider> bindingProviders)
    {
        return new HttpFunctionDescriptorProvider(host, _scriptHostOptions, bindingProviders, _dispatcher, _loggerFactory, _applicationLifetime, _httpWorkerOptions.InitializationTimeout);
    }

    public FunctionDescriptorProvider CreateMultiWorkerDescriptorProvider(ScriptHost host, ICollection<IScriptBindingProvider> bindingProviders)
    {
        var workerOptions = _languageWorkerOptionsMonitor.CurrentValue;
        return new MultiLanguageFunctionDescriptorProvider(host, workerOptions.WorkerConfigs, _scriptHostOptions, bindingProviders,
                    _dispatcher, _loggerFactory, _applicationLifetime, workerOptions.WorkerConfigs.Max(wc => wc.CountOptions.InitializationTimeout));
    }

    public FunctionDescriptorProvider CreateWorkerDescriptorProvider(ScriptHost host, string workerRuntime, ICollection<IScriptBindingProvider> bindingProviders)
    {
        var workerConfig = _languageWorkerOptionsMonitor.CurrentValue.WorkerConfigs?.FirstOrDefault(c => c.Description.Language.Equals(workerRuntime, StringComparison.OrdinalIgnoreCase));

        // If there's no worker config, use the default (for legacy behavior; mostly for tests).
        TimeSpan initializationTimeout = workerConfig?.CountOptions?.InitializationTimeout ?? WorkerProcessCountOptions.DefaultInitializationTimeout;

        return new RpcFunctionDescriptorProvider(host, workerRuntime, _scriptHostOptions, bindingProviders, _dispatcher, _loggerFactory, _applicationLifetime, initializationTimeout);
    }
}