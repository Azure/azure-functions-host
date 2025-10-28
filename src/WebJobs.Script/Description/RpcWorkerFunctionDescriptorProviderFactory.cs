// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.Azure.WebJobs.Script.ExtensionBundle;
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
    private readonly IScriptEventManager _eventManager;
    private readonly bool _isExtensionBundleConfigured;
    private readonly ILoggerFactory _loggerFactory;

    public RpcWorkerFunctionDescriptorProviderFactory(IFunctionInvocationDispatcherFactory dispatcherFactory, IApplicationLifetime applicationLifetime, IOptions<ScriptJobHostOptions> scriptHostOptions,
                    IOptions<HttpWorkerOptions> httpWorkerOptions, IOptionsMonitor<LanguageWorkerOptions> languageWorkerOptionsMonitor, IScriptEventManager eventManager, IExtensionBundleManager bundleManager,
                    ILoggerFactory loggerFactory)
    {
        _dispatcher = dispatcherFactory.GetFunctionDispatcher();
        _applicationLifetime = applicationLifetime;
        _httpWorkerOptions = httpWorkerOptions.Value;
        _scriptHostOptions = scriptHostOptions.Value;
        _languageWorkerOptionsMonitor = languageWorkerOptionsMonitor;
        _eventManager = eventManager;
        _isExtensionBundleConfigured = bundleManager.IsExtensionBundleConfigured();
        _loggerFactory = loggerFactory;
    }

    public FunctionDescriptorProvider CreateHttpDescriptorProvider(ICollection<IScriptBindingProvider> bindingProviders)
    {
        return new HttpFunctionDescriptorProvider(_scriptHostOptions, bindingProviders, _dispatcher, _loggerFactory, _applicationLifetime, _eventManager, _isExtensionBundleConfigured, _httpWorkerOptions.InitializationTimeout);
    }

    public FunctionDescriptorProvider CreateMultiWorkerDescriptorProvider(ICollection<IScriptBindingProvider> bindingProviders)
    {
        var workerOptions = _languageWorkerOptionsMonitor.CurrentValue;
        return new MultiLanguageFunctionDescriptorProvider( workerOptions.WorkerConfigs, _scriptHostOptions, bindingProviders,
                _dispatcher, _loggerFactory, _applicationLifetime, _eventManager, _isExtensionBundleConfigured, workerOptions.WorkerConfigs.Max(wc => wc.CountOptions.InitializationTimeout));
    }

    public FunctionDescriptorProvider CreateWorkerDescriptorProvider(string workerRuntime, ICollection<IScriptBindingProvider> bindingProviders)
    {
        var workerConfig = _languageWorkerOptionsMonitor.CurrentValue.WorkerConfigs?.FirstOrDefault(c => c.Description.Language.Equals(workerRuntime, StringComparison.OrdinalIgnoreCase));

        // If there's no worker config, use the default (for legacy behavior; mostly for tests).
        TimeSpan initializationTimeout = workerConfig?.CountOptions?.InitializationTimeout ?? WorkerProcessCountOptions.DefaultInitializationTimeout;

        return new RpcFunctionDescriptorProvider(workerRuntime, _scriptHostOptions, bindingProviders, _dispatcher, _loggerFactory, _applicationLifetime, _eventManager, _isExtensionBundleConfigured, initializationTimeout);
    }
}