// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.Functions.WorkerModel.Descriptor;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OutOfProcModel.Abstractions.Worker;

namespace Microsoft.Azure.WebJobs.Script.Description;

internal class WorkerModelFunctionDescriptorProviderFactory : IWorkerFunctionDescriptorProviderFactory
{
    // private readonly IFunctionInvocationDispatcher _dispatcher;
    // private readonly IApplicationLifetime _applicationLifetime;
    // private readonly HttpWorkerOptions _httpWorkerOptions;
    private readonly ScriptJobHostOptions _scriptHostOptions;
    // private readonly IOptionsMonitor<LanguageWorkerOptions> _languageWorkerOptionsMonitor;
    private readonly ILoggerFactory _loggerFactory;
    // private readonly ILogger _logger;
    private readonly IWorkerResolver _workerResolver;

    public WorkerModelFunctionDescriptorProviderFactory(IWorkerResolver workerResolver, IOptions<ScriptJobHostOptions> scriptHostOptions, ILoggerFactory loggerFactory)
    {
        //_httpWorkerOptions = httpWorkerOptions.Value;
        _scriptHostOptions = scriptHostOptions.Value;
        // _languageWorkerOptionsMonitor = languageWorkerOptionsMonitor;
        _loggerFactory = loggerFactory;
        // _logger = _loggerFactory.CreateLogger(LogCategories.Startup);
        _workerResolver = workerResolver;
    }

    public FunctionDescriptorProvider CreateMultiWorkerDescriptorProvider(ScriptHost host, ICollection<IScriptBindingProvider> bindingProviders)
    {
        throw new NotImplementedException();

        //var workerOptions = _languageWorkerOptionsMonitor.CurrentValue;
        //return new MultiLanguageFunctionDescriptorProvider(host, workerOptions.WorkerConfigs, _scriptHostOptions, bindingProviders,
        //            _dispatcher, _loggerFactory, _applicationLifetime, workerOptions.WorkerConfigs.Max(wc => wc.CountOptions.InitializationTimeout));
    }

    public FunctionDescriptorProvider CreateWorkerDescriptorProvider(ScriptHost host, string workerRuntime, ICollection<IScriptBindingProvider> bindingProviders)
    {
        //if (_httpWorkerOptions.Description is not null)
        //{
        //    _logger.LogDebug(new EventId(414, "AddingDescriptorProviderForHttpWorker"), "Adding Function descriptor provider for HttpWorker.");
        //    return new HttpFunctionDescriptorProvider(host, _scriptHostOptions, bindingProviders, _dispatcher, _loggerFactory, _applicationLifetime, _httpWorkerOptions.InitializationTimeout);
        //}

        // var workerConfig = _languageWorkerOptionsMonitor.CurrentValue.WorkerConfigs?.FirstOrDefault(c => c.Description.Language.Equals(workerRuntime, StringComparison.OrdinalIgnoreCase));

        // If there's no worker config, use the default (for legacy behavior; mostly for tests).
        // TimeSpan initializationTimeout = workerConfig?.CountOptions?.InitializationTimeout ?? WorkerProcessCountOptions.DefaultInitializationTimeout;

        return new WorkerModelFunctionDescriptorProvider(host, _scriptHostOptions, bindingProviders, _loggerFactory, _workerResolver);
    }
}