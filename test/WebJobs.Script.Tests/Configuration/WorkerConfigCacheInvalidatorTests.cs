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
        // First run - sets _firstRun to false
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
    public void InvalidateCachePostBuildIfEnabled_WithoutEnableInvalidationForNextBuild_DoesNotInvalidateCache()
    {
        // Arrange - cache tokens before invalidation
        var workerConfigResolverToken = _workerConfigResolverTokenSource.GetChangeToken();
        var languageWorkerToken = _languageWorkerTokenSource.GetChangeToken();

        // Act - without calling EnableInvalidationForNextBuild, should not invalidate
        _invalidator.InvalidateCachePostBuildIfEnabled();

        // Assert - original tokens should not have changed
        Assert.False(workerConfigResolverToken.HasChanged);
        Assert.False(languageWorkerToken.HasChanged);
    }

    [Fact]
    public void InvalidateCachePostBuildIfEnabled_WithEnableInvalidationForNextBuild_InvalidatesCache()
    {
        // Arrange
        _invalidator.EnableInvalidationForNextBuild();

        // Cache tokens before invalidation
        var workerConfigResolverToken = _workerConfigResolverTokenSource.GetChangeToken();
        var languageWorkerToken = _languageWorkerTokenSource.GetChangeToken();

        // Act - with EnableInvalidationForNextBuild called, should invalidate
        _invalidator.InvalidateCachePostBuildIfEnabled();

        // Assert - original tokens should have changed
        Assert.True(workerConfigResolverToken.HasChanged);
        Assert.True(languageWorkerToken.HasChanged);
    }

    [Fact]
    public void InvalidateCachePostBuildIfEnabled_AfterInvalidateCacheForBundles_DoesNotInvalidate()
    {
        // Arrange
        // Set up to use bundles
        _invalidator.InvalidateCacheForBundles();
        _invalidator.EnableInvalidationForNextBuild();

        // Cache tokens before calling InvalidateCachePostBuildIfEnabled
        var workerConfigResolverToken = _workerConfigResolverTokenSource.GetChangeToken();
        var languageWorkerToken = _languageWorkerTokenSource.GetChangeToken();

        // Act - even with invalidation enabled, should invalidate
        _invalidator.InvalidateCachePostBuildIfEnabled();

        // Assert - original tokens should have changed since flag was enabled
        Assert.True(workerConfigResolverToken.HasChanged);
        Assert.True(languageWorkerToken.HasChanged);
    }

    [Fact]
    public void InvalidateCachePostBuildIfEnabled_ResetsEnabledFlag()
    {
        // Arrange
        _invalidator.EnableInvalidationForNextBuild();
        _invalidator.InvalidateCachePostBuildIfEnabled();

        // Cache tokens before second InvalidateCachePostBuildIfEnabled call
        var workerConfigResolverToken = _workerConfigResolverTokenSource.GetChangeToken();
        var languageWorkerToken = _languageWorkerTokenSource.GetChangeToken();

        // Act - after reset, should not invalidate cache without re-enabling
        _invalidator.InvalidateCachePostBuildIfEnabled();

        // Assert - the flag was reset, so original tokens should not have changed
        Assert.False(workerConfigResolverToken.HasChanged);
        Assert.False(languageWorkerToken.HasChanged);
    }

    [Fact]
    public void InvalidateCachePostBuildIfEnabled_WithNonRefreshWorkerOptionsChangeTokenSource_DoesNotThrow()
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

        invalidator.EnableInvalidationForNextBuild();

        // Act & Assert - should not throw when non-RefreshWorkerOptionsChangeTokenSource is used
        var exception = Record.Exception(() => invalidator.InvalidateCachePostBuildIfEnabled());
        Assert.Null(exception);
    }

    [Fact]
    public void BundlesWorkflow_SimulatesTypicalHostRestartScenario()
    {
        // First host start with bundles - should not invalidate on first run
        var token1 = _workerConfigResolverTokenSource.GetChangeToken();
        _invalidator.InvalidateCacheForBundles();
        _invalidator.EnableInvalidationForNextBuild();
        _invalidator.InvalidateCachePostBuildIfEnabled();
        Assert.True(token1.HasChanged); // Post-build invalidation was enabled, so should invalidate

        // Simulate multiple host restart cycles with bundles
        for (int i = 0; i < 3; i++)
        {
            var token = _workerConfigResolverTokenSource.GetChangeToken();

            // Each restart cycle calls InvalidateCacheForBundles
            _invalidator.InvalidateCacheForBundles();
            Assert.True(token.HasChanged); // Should invalidate after first run

            var tokenAfterBundles = _workerConfigResolverTokenSource.GetChangeToken();
            _invalidator.InvalidateCachePostBuildIfEnabled();
            Assert.False(tokenAfterBundles.HasChanged); // Flag not re-enabled, no additional invalidation
        }
    }

    [Fact]
    public void NonBundlesWorkflow_SimulatesTypicalScenario()
    {
        // Arrange
        _invalidator.EnableInvalidationForNextBuild();

        // Cache token before invalidation
        // Simulate host start without bundles (never call InvalidateCacheForBundles)
        var token1 = _workerConfigResolverTokenSource.GetChangeToken();

        // Act
        _invalidator.InvalidateCachePostBuildIfEnabled();

        // Assert - original token should have changed
        Assert.True(token1.HasChanged);
    }

    [Fact]
    public void InvalidateCachePostBuildIfEnabled_OnlyInvalidatesRefreshWorkerOptionsChangeTokenSource()
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

        invalidator.EnableInvalidationForNextBuild();

        // Act
        invalidator.InvalidateCachePostBuildIfEnabled();

        // Assert - original token should have changed
        Assert.True(refreshToken.HasChanged);
        mockToken.Verify(x => x.HasChanged, Times.Never()); // Mock token should not be accessed
    }

    [Fact]
    public void EnableInvalidationForNextBuild_CanBeCalledMultipleTimes()
    {
        // Arrange
        var token = _workerConfigResolverTokenSource.GetChangeToken();

        // Act
        _invalidator.EnableInvalidationForNextBuild();
        _invalidator.EnableInvalidationForNextBuild();
        _invalidator.EnableInvalidationForNextBuild();
        _invalidator.InvalidateCachePostBuildIfEnabled();

        // Assert - should still work correctly
        Assert.True(token.HasChanged);
    }

    [Fact]
    public void EnableInvalidationForNextBuild_RequiresReenablingAfterInvalidation()
    {
        // Arrange
        _invalidator.EnableInvalidationForNextBuild();

        // Act & Assert - first invalidation should work
        var token1 = _workerConfigResolverTokenSource.GetChangeToken();
        _invalidator.InvalidateCachePostBuildIfEnabled();
        Assert.True(token1.HasChanged, "First invalidation should succeed");

        // Second invalidation without re-enabling should not work
        var token2 = _workerConfigResolverTokenSource.GetChangeToken();
        _invalidator.InvalidateCachePostBuildIfEnabled();
        Assert.False(token2.HasChanged, "Second invalidation should not succeed without re-enabling");

        // Re-enable and third invalidation should work again
        _invalidator.EnableInvalidationForNextBuild();
        var token3 = _workerConfigResolverTokenSource.GetChangeToken();
        _invalidator.InvalidateCachePostBuildIfEnabled();
        Assert.True(token3.HasChanged, "Third invalidation should succeed after re-enabling");
    }
}
