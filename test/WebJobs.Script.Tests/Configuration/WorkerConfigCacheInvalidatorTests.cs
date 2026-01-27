// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration;

public class WorkerConfigCacheInvalidatorTests
{
    private readonly RefreshWorkerOptionsChangeTokenSource<WorkerConfigurationResolverOptions> _workerConfigResolverTokenSource;
    private readonly RefreshWorkerOptionsChangeTokenSource<LanguageWorkerOptions> _languageWorkerTokenSource;
    private readonly WorkerConfigCacheInvalidator _invalidator;

    public WorkerConfigCacheInvalidatorTests()
    {
        _workerConfigResolverTokenSource = new RefreshWorkerOptionsChangeTokenSource<WorkerConfigurationResolverOptions>();
        _languageWorkerTokenSource = new RefreshWorkerOptionsChangeTokenSource<LanguageWorkerOptions>();
        _invalidator = new WorkerConfigCacheInvalidator(
            _workerConfigResolverTokenSource,
            _languageWorkerTokenSource);
    }

    [Fact]
    public void InvalidateCacheForBundles_FirstRun_DoesNotInvalidateCache()
    {
        // Arrange - cache tokens before invalidation
        var workerConfigResolverToken = _workerConfigResolverTokenSource.GetChangeToken();
        var languageWorkerToken = _languageWorkerTokenSource.GetChangeToken();

        // Act
        _invalidator.InvalidateCacheForBundles();

        // Assert - original tokens should not have changed on first run
        Assert.False(workerConfigResolverToken.HasChanged);
        Assert.False(languageWorkerToken.HasChanged);
    }

    [Fact]
    public void InvalidateCacheForBundles_SecondRun_InvalidatesCache()
    {
        // Arrange
        // First run - sets _firstRun to false and _usingBundles to true
        _invalidator.InvalidateCacheForBundles();

        // Cache tokens before second invalidation
        var workerConfigResolverToken = _workerConfigResolverTokenSource.GetChangeToken();
        var languageWorkerToken = _languageWorkerTokenSource.GetChangeToken();

        // Act - second run should invalidate cache
        _invalidator.InvalidateCacheForBundles();

        // Assert - original tokens should have changed on second run
        Assert.True(workerConfigResolverToken.HasChanged);
        Assert.True(languageWorkerToken.HasChanged);
    }

    [Fact]
    public void InvalidateCacheForBundles_MultipleRuns_InvalidatesCacheAfterFirst()
    {
        // Arrange
        // First run
        _invalidator.InvalidateCacheForBundles();

        // Second run
        _invalidator.InvalidateCacheForBundles();

        // Cache tokens before third invalidation
        var workerConfigResolverToken = _workerConfigResolverTokenSource.GetChangeToken();
        var languageWorkerToken = _languageWorkerTokenSource.GetChangeToken();

        // Act - third run should also invalidate cache
        _invalidator.InvalidateCacheForBundles();

        // Assert - original tokens should have changed
        Assert.True(workerConfigResolverToken.HasChanged);
        Assert.True(languageWorkerToken.HasChanged);
    }

    [Fact]
    public void InvalidateCacheIfNotUsingBundles_WhenNotUsingBundles_InvalidatesCache()
    {
        // Arrange - cache tokens before invalidation
        var workerConfigResolverToken = _workerConfigResolverTokenSource.GetChangeToken();
        var languageWorkerToken = _languageWorkerTokenSource.GetChangeToken();

        // Act - not using bundles, should invalidate
        _invalidator.InvalidateCacheIfNotUsingBundles();

        // Assert - original tokens should have changed
        Assert.True(workerConfigResolverToken.HasChanged);
        Assert.True(languageWorkerToken.HasChanged);
    }

    [Fact]
    public void InvalidateCacheIfNotUsingBundles_WhenUsingBundles_DoesNotInvalidateCache()
    {
        // Arrange
        // Set up to use bundles
        _invalidator.InvalidateCacheForBundles();

        // Cache tokens before calling InvalidateCacheIfNotUsingBundles
        var workerConfigResolverToken = _workerConfigResolverTokenSource.GetChangeToken();
        var languageWorkerToken = _languageWorkerTokenSource.GetChangeToken();

        // Act - using bundles, should not invalidate
        _invalidator.InvalidateCacheIfNotUsingBundles();

        // Assert - original tokens should not have changed
        Assert.False(workerConfigResolverToken.HasChanged);
        Assert.False(languageWorkerToken.HasChanged);
    }

    [Fact]
    public void InvalidateCacheIfNotUsingBundles_ResetsUsingBundlesFlag()
    {
        // Arrange
        // Set up to use bundles
        _invalidator.InvalidateCacheForBundles();
        _invalidator.InvalidateCacheIfNotUsingBundles();

        // Cache tokens before second InvalidateCacheIfNotUsingBundles call
        var workerConfigResolverToken = _workerConfigResolverTokenSource.GetChangeToken();
        var languageWorkerToken = _languageWorkerTokenSource.GetChangeToken();

        // Act - after reset, should invalidate cache again
        _invalidator.InvalidateCacheIfNotUsingBundles();

        // Assert - the flag was reset, so original tokens should have changed
        Assert.True(workerConfigResolverToken.HasChanged);
        Assert.True(languageWorkerToken.HasChanged);
    }

    [Fact]
    public void InvalidateCache_WithNonRefreshWorkerOptionsChangeTokenSource_DoesNotThrow()
    {
        // Arrange
        var mockWorkerConfigResolverTokenSource = new Mock<IOptionsChangeTokenSource<WorkerConfigurationResolverOptions>>();
        var mockLanguageWorkerTokenSource = new Mock<IOptionsChangeTokenSource<LanguageWorkerOptions>>();

        mockWorkerConfigResolverTokenSource.Setup(x => x.GetChangeToken())
            .Returns(Mock.Of<IChangeToken>());
        mockLanguageWorkerTokenSource.Setup(x => x.GetChangeToken())
            .Returns(Mock.Of<IChangeToken>());

        var invalidator = new WorkerConfigCacheInvalidator(
            mockWorkerConfigResolverTokenSource.Object,
            mockLanguageWorkerTokenSource.Object);

        // Act & Assert - should not throw when non-RefreshWorkerOptionsChangeTokenSource is used
        var exception = Record.Exception(() => invalidator.InvalidateCacheIfNotUsingBundles());
        Assert.Null(exception);
    }

    [Fact]
    public void BundlesWorkflow_SimulatesTypicalHostRestartScenario()
    {
        // First host start with bundles - should not invalidate on first run
        var token1 = _workerConfigResolverTokenSource.GetChangeToken();
        _invalidator.InvalidateCacheForBundles();
        _invalidator.InvalidateCacheIfNotUsingBundles();
        Assert.False(token1.HasChanged); // First run doesn't invalidate, still using bundles

        // Simulate multiple host restart cycles with bundles
        for (int i = 0; i < 3; i++)
        {
            var token = _workerConfigResolverTokenSource.GetChangeToken();

            // Each restart cycle calls both methods
            _invalidator.InvalidateCacheForBundles();
            Assert.True(token.HasChanged); // Should invalidate after first run

            var tokenAfterBundles = _workerConfigResolverTokenSource.GetChangeToken();
            _invalidator.InvalidateCacheIfNotUsingBundles();
            Assert.False(tokenAfterBundles.HasChanged); // Still using bundles, no additional invalidation
        }
    }

    [Fact]
    public void NonBundlesWorkflow_SimulatesTypicalScenario()
    {
        // Arrange - cache token before invalidation
        // Simulate host start without bundles (never call InvalidateCacheForBundles)
        var token1 = _workerConfigResolverTokenSource.GetChangeToken();

        // Act
        _invalidator.InvalidateCacheIfNotUsingBundles();

        // Assert - original token should have changed
        Assert.True(token1.HasChanged);
    }

    [Fact]
    public void InvalidateCache_OnlyInvalidatesRefreshWorkerOptionsChangeTokenSource()
    {
        // Arrange
        var mockTokenSource = new Mock<IOptionsChangeTokenSource<LanguageWorkerOptions>>();
        var mockToken = new Mock<IChangeToken>();

        mockTokenSource.Setup(x => x.GetChangeToken()).Returns(mockToken.Object);

        // Cache token before invalidation
        var refreshToken = _workerConfigResolverTokenSource.GetChangeToken();

        var invalidator = new WorkerConfigCacheInvalidator(
            _workerConfigResolverTokenSource,
            mockTokenSource.Object);

        // Act
        invalidator.InvalidateCacheIfNotUsingBundles();

        // Assert - original token should have changed
        Assert.True(refreshToken.HasChanged);
        mockToken.Verify(x => x.HasChanged, Times.Never()); // Mock token should not be accessed
    }
}
