// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Configuration;

public class WorkerConfigCacheInvalidator
{
    private readonly IOptionsChangeTokenSource<WorkerConfigurationResolverOptions> _workerConfigResolverOptionsChangeTokenSource;
    private readonly IOptionsChangeTokenSource<LanguageWorkerOptions> _languageWorkerOptionsChangeTokenSource;

    private bool _usingBundles = false;
    private bool _firstRun = true;

    public WorkerConfigCacheInvalidator(
        IOptionsChangeTokenSource<WorkerConfigurationResolverOptions> workerConfigResolverOptionsChangeTokenSource,
        IOptionsChangeTokenSource<LanguageWorkerOptions> languageWorkerOptionsChangeTokenSource)
    {
        _workerConfigResolverOptionsChangeTokenSource = workerConfigResolverOptionsChangeTokenSource;
        _languageWorkerOptionsChangeTokenSource = languageWorkerOptionsChangeTokenSource;
    }

    public void InvalidateCacheForBundles()
    {
        _usingBundles = true;

        if (!_firstRun)
        {
            InvalidateCache();
        }
        else
        {
            _firstRun = false;
        }
    }

    public void InvalidateCacheIfNotUsingBundles()
    {
        if (!_usingBundles)
        {
            InvalidateCache();
        }

        // Reset for future restarts
        _usingBundles = false;
    }

    private void InvalidateCache()
    {
        if (_workerConfigResolverOptionsChangeTokenSource is HostBuiltChangeTokenSource<WorkerConfigurationResolverOptions> { } hostBuiltChangeTokenResolverOptions)
        {
            hostBuiltChangeTokenResolverOptions.TriggerChange();
        }

        if (_languageWorkerOptionsChangeTokenSource is HostBuiltChangeTokenSource<LanguageWorkerOptions> { } hostBuiltChangeTokenSource)
        {
            hostBuiltChangeTokenSource.TriggerChange();
        }
    }
}
