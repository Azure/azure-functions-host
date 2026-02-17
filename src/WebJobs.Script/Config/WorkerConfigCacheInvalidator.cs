// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Configuration;

/// <summary>
/// Manages cache invalidation for worker configuration options during host specialization and restart scenarios.
/// This class coordinates the invalidation of <see cref="WorkerConfigurationResolverOptions"/> and
/// <see cref="LanguageWorkerOptions"/> caches to ensure proper reconfiguration when extension bundles are
/// loaded or when the host transitions from placeholder to specialized mode.
/// </summary>
/// <remarks>
/// <para>
/// The invalidator operates in two main scenarios:
/// </para>
/// <list type="number">
/// <item>
/// <description>
/// <b>Bundle-based invalidation</b>: When extension bundles are loaded, the first call to
/// <see cref="InvalidateCacheForBundles"/> sets up tracking but doesn't invalidate. Subsequent calls
/// trigger cache invalidation to ensure worker configurations reflect the newly available bundle binaries.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Post-build invalidation</b>: For scenarios without bundles, the host must explicitly enable
/// post-build invalidation by calling <see cref="EnableInvalidationForNextBuild"/>. The cache is then
/// invalidated when <see cref="InvalidateCachePostBuildIfEnabled"/> is called, typically after the
/// host build completes.
/// </description>
/// </item>
/// </list>
/// <para>
/// This design prevents premature cache invalidation while ensuring that worker configurations are
/// properly refreshed when needed, particularly during host specialization in consumption plans.
/// </para>
/// </remarks>
public sealed class WorkerConfigCacheInvalidator
{
    private readonly IOptionsChangeTokenSource<WorkerConfigurationResolverOptions> _workerConfigResolverOptionsChangeTokenSource;
    private readonly IOptionsChangeTokenSource<LanguageWorkerOptions> _languageWorkerOptionsChangeTokenSource;

    private bool _postBuildInvalidationEnabled = false;
    private bool _firstRun = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkerConfigCacheInvalidator"/> class.
    /// </summary>
    /// <param name="workerConfigResolverOptionsChangeTokenSource">
    /// The change token source for <see cref="WorkerConfigurationResolverOptions"/>.
    /// Must be a <see cref="RefreshWorkerOptionsChangeTokenSource{TOptions}"/> to support invalidation.
    /// </param>
    /// <param name="languageWorkerOptionsChangeTokenSource">
    /// The change token source for <see cref="LanguageWorkerOptions"/>.
    /// Must be a <see cref="RefreshWorkerOptionsChangeTokenSource{TOptions}"/> to support invalidation.
    /// </param>
    public WorkerConfigCacheInvalidator(
        IOptionsChangeTokenSource<WorkerConfigurationResolverOptions> workerConfigResolverOptionsChangeTokenSource,
        IOptionsChangeTokenSource<LanguageWorkerOptions> languageWorkerOptionsChangeTokenSource)
    {
        _workerConfigResolverOptionsChangeTokenSource = workerConfigResolverOptionsChangeTokenSource;
        _languageWorkerOptionsChangeTokenSource = languageWorkerOptionsChangeTokenSource;
    }

    /// <summary>
    /// Invalidates the worker configuration cache when extension bundles are being used.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method implements deferred invalidation for bundle scenarios:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <b>First call</b>: Sets up tracking but does not invalidate the cache. This allows the host
    /// to complete its initial configuration with bundles.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <b>Subsequent calls</b>: Triggers cache invalidation by calling <see cref="InvalidateCache"/>,
    /// ensuring that worker configurations are refreshed to reflect bundle changes or host restarts.
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// This pattern is typically used when extension bundles are configured, as the bundles need to be
    /// downloaded and extracted before worker configurations can be properly resolved.
    /// </para>
    /// </remarks>
    public void InvalidateCacheForBundles()
    {
        if (!_firstRun)
        {
            InvalidateCache();
        }

        _firstRun = false;
    }

    /// <summary>
    /// Enables cache invalidation to occur when <see cref="InvalidateCachePostBuildIfEnabled"/> is called.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method sets a flag that allows the next call to <see cref="InvalidateCachePostBuildIfEnabled"/>
    /// to invalidate the cache. This is typically called before a host build operation when extension bundles
    /// are not being used, allowing the cache to be refreshed after the build completes.
    /// </para>
    /// <para>
    /// The flag is automatically reset after <see cref="InvalidateCachePostBuildIfEnabled"/> is called,
    /// requiring this method to be called again for subsequent invalidations.
    /// </para>
    /// </remarks>
    public void EnableInvalidationForNextBuild()
    {
        _postBuildInvalidationEnabled = true;
    }

    /// <summary>
    /// Invalidates the worker configuration cache if post-build invalidation was previously enabled
    /// by calling <see cref="EnableInvalidationForNextBuild"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method checks if post-build invalidation is enabled and, if so, invalidates the cache by
    /// calling <see cref="InvalidateCache"/>. After checking, the enabled flag is always reset to false,
    /// regardless of whether invalidation occurred.
    /// </para>
    /// <para>
    /// This pattern ensures that cache invalidation only happens when explicitly requested and prevents
    /// unintended invalidations across multiple host restart cycles. It is typically called after a host
    /// build completes for scenarios that don't use extension bundles.
    /// </para>
    /// </remarks>
    public void InvalidateCachePostBuildIfEnabled()
    {
        if (_postBuildInvalidationEnabled)
        {
            InvalidateCache();
        }

        // Reset for future restarts
        _postBuildInvalidationEnabled = false;
    }

    private void InvalidateCache()
    {
        if (_workerConfigResolverOptionsChangeTokenSource is RefreshWorkerOptionsChangeTokenSource<WorkerConfigurationResolverOptions> { } refreshWorkerConfigResolverOptions)
        {
            refreshWorkerConfigResolverOptions.TriggerChange();
        }

        if (_languageWorkerOptionsChangeTokenSource is RefreshWorkerOptionsChangeTokenSource<LanguageWorkerOptions> { } refreshLanguageWorkerOptions)
        {
            refreshLanguageWorkerOptions.TriggerChange();
        }
    }
}
