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

public sealed class WorkerLinkSpecializationGateTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private readonly Mock<IOptionsMonitor<ScriptApplicationHostOptions>> _options = new(MockBehavior.Strict);
    private readonly Mock<IDisposable> _registration = new();
    private readonly TaskCompletionSource _subscribed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ScriptApplicationHostOptions _current = TestScriptApplicationHostOptions.CreateStandby();
    private Action<ScriptApplicationHostOptions, string?>? _changed;

    public WorkerLinkSpecializationGateTests()
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
    public void Constructor_InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new WorkerLinkSpecializationGate(null!));
        Assert.Throws<ArgumentNullException>(() => new WorkerLinkSpecializationGate(null!, TestTimeout));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkerLinkSpecializationGate(_options.Object, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkerLinkSpecializationGate(_options.Object, TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public async Task WaitForSpecializedConfiguration_AlreadySpecialized_ReturnsTrueWithoutSubscribing()
    {
        _current = new ScriptApplicationHostOptions();

        Task<bool> wait = CreateGate(TestTimeout).WaitForSpecializedConfigurationAsync(CancellationToken.None);

        Assert.True(wait.IsCompletedSuccessfully);
        Assert.True(await wait);
        _options.Verify(options => options.OnChange(It.IsAny<Action<ScriptApplicationHostOptions, string?>>()), Times.Never);
    }

    [Fact]
    public async Task WaitForSpecializedConfiguration_Specializes_ReturnsTrueAndDisposesRegistration()
    {
        Task<bool> wait = CreateGate(TestTimeout).WaitForSpecializedConfigurationAsync(CancellationToken.None);
        await _subscribed.Task.WaitAsync(TestTimeout);

        Assert.False(wait.IsCompleted);
        Specialize();

        Assert.True(await wait.WaitAsync(TestTimeout));
        _registration.Verify(registration => registration.Dispose(), Times.Once);
    }

    [Fact]
    public async Task WaitForSpecializedConfiguration_SpecializedBeforeListenerRegistered_ReturnsTrue()
    {
        _options.SetupSequence(options => options.CurrentValue)
            .Returns(TestScriptApplicationHostOptions.CreateStandby())
            .Returns(new ScriptApplicationHostOptions());

        bool specialized = await CreateGate(TestTimeout).WaitForSpecializedConfigurationAsync(CancellationToken.None)
            .WaitAsync(TestTimeout);

        Assert.True(specialized);
        _registration.Verify(registration => registration.Dispose(), Times.Once);
    }

    [Fact]
    public async Task WaitForSpecializedConfiguration_ChangeKeepsPlaceholderConfiguration_ReturnsFalseAfterTimeout()
    {
        Task<bool> wait = CreateGate(TimeSpan.FromMilliseconds(100)).WaitForSpecializedConfigurationAsync(CancellationToken.None);
        await _subscribed.Task.WaitAsync(TestTimeout);

        _changed!(_current, Options.DefaultName);

        Assert.False(await wait.WaitAsync(TestTimeout));
        _registration.Verify(registration => registration.Dispose(), Times.Once);
    }

    [Fact]
    public async Task WaitForSpecializedConfiguration_Canceled_ThrowsAndDisposesRegistration()
    {
        using CancellationTokenSource cancellation = new();
        Task<bool> wait = CreateGate(TestTimeout).WaitForSpecializedConfigurationAsync(cancellation.Token);
        await _subscribed.Task.WaitAsync(TestTimeout);

        cancellation.Cancel();

        OperationCanceledException exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => wait.WaitAsync(TestTimeout));
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        _registration.Verify(registration => registration.Dispose(), Times.Once);
    }

    private WorkerLinkSpecializationGate CreateGate(TimeSpan timeout) => new(_options.Object, timeout);

    private void Specialize()
    {
        ScriptApplicationHostOptions specialized = new();
        Volatile.Write(ref _current, specialized);
        _changed!(specialized, Options.DefaultName);
    }
}
