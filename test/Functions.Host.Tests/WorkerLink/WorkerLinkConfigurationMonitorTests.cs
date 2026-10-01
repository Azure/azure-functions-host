// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Host.WorkerLink;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Azure.Functions.Host.Tests.WorkerLink;

public sealed class WorkerLinkConfigurationMonitorTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private readonly Mock<IOptionsMonitor<ScriptApplicationHostOptions>> _options = new(MockBehavior.Strict);
    private readonly Mock<IDisposable> _registration = new();
    private readonly TaskCompletionSource _subscribed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ScriptApplicationHostOptions _current = TestScriptApplicationHostOptions.CreateStandby();
    private Action<ScriptApplicationHostOptions, string?>? _changed;

    public WorkerLinkConfigurationMonitorTests()
    {
        _options.SetupGet(options => options.CurrentValue).Returns(() => Volatile.Read(ref _current));
        _options.Setup(options => options.OnChange(It.IsAny<Action<ScriptApplicationHostOptions, string?>>()))
            .Returns((Action<ScriptApplicationHostOptions, string?> listener) =>
            {
                _changed = listener;
                _subscribed.TrySetResult();
                return _registration.Object;
            });
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new WorkerLinkConfigurationMonitor(null!));
    }

    [Fact]
    public async Task WaitForSpecializedConfiguration_AlreadySpecialized_CompletesWithoutSubscribing()
    {
        _current = new ScriptApplicationHostOptions();

        Task wait = CreateMonitor().WaitForSpecializedConfigurationAsync(CancellationToken.None);

        Assert.True(wait.IsCompletedSuccessfully);
        await wait;
        _options.Verify(options => options.OnChange(It.IsAny<Action<ScriptApplicationHostOptions, string?>>()), Times.Never);
    }

    [Fact]
    public async Task WaitForSpecializedConfiguration_Specializes_CompletesAndDisposesRegistration()
    {
        Task wait = CreateMonitor().WaitForSpecializedConfigurationAsync(CancellationToken.None);
        await _subscribed.Task.WaitAsync(TestTimeout);

        Assert.False(wait.IsCompleted);
        Specialize();

        await wait.WaitAsync(TestTimeout);
        _registration.Verify(registration => registration.Dispose(), Times.Once);
    }

    [Fact]
    public async Task WaitForSpecializedConfiguration_SpecializedBeforeListenerRegistered_Completes()
    {
        _options.SetupSequence(options => options.CurrentValue)
            .Returns(TestScriptApplicationHostOptions.CreateStandby())
            .Returns(new ScriptApplicationHostOptions());

        await CreateMonitor().WaitForSpecializedConfigurationAsync(CancellationToken.None).WaitAsync(TestTimeout);

        _registration.Verify(registration => registration.Dispose(), Times.Once);
    }

    [Fact]
    public async Task WaitForSpecializedConfiguration_ChangeKeepsPlaceholderConfiguration_KeepsWaiting()
    {
        using CancellationTokenSource cancellation = new();
        Task wait = CreateMonitor().WaitForSpecializedConfigurationAsync(cancellation.Token);
        await _subscribed.Task.WaitAsync(TestTimeout);

        _changed!(_current, Options.DefaultName);

        await Assert.ThrowsAsync<TimeoutException>(() => wait.WaitAsync(TimeSpan.FromMilliseconds(100)));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait.WaitAsync(TestTimeout));
        _registration.Verify(registration => registration.Dispose(), Times.Once);
    }

    [Fact]
    public async Task WaitForSpecializedConfiguration_Canceled_ThrowsAndDisposesRegistration()
    {
        using CancellationTokenSource cancellation = new();
        Task wait = CreateMonitor().WaitForSpecializedConfigurationAsync(cancellation.Token);
        await _subscribed.Task.WaitAsync(TestTimeout);

        cancellation.Cancel();

        OperationCanceledException exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => wait.WaitAsync(TestTimeout));
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        _registration.Verify(registration => registration.Dispose(), Times.Once);
    }

    private WorkerLinkConfigurationMonitor CreateMonitor() => new(_options.Object);

    private void Specialize()
    {
        ScriptApplicationHostOptions specialized = new();
        Volatile.Write(ref _current, specialized);
        _changed!(specialized, Options.DefaultName);
    }
}
