// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Management;
using Azure.Functions.WorkerProxy.State;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests.Management;

public class ManagementApiHandlersTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void GetWorkerReady_TracksStartStreamAndTerminationWithoutChangingState()
    {
        WorkerPodStateManager manager = CreateManager();
        WorkerPodState initial = manager.State;

        Assert.Equal(503, Assert.IsType<StatusCodeHttpResult>(ManagementApiHandlers.GetWorkerReady(manager)).StatusCode);
        Assert.Same(initial, manager.State);
        manager.OnWorkerAttached(1);
        Assert.Equal(503, Assert.IsType<StatusCodeHttpResult>(ManagementApiHandlers.GetWorkerReady(manager)).StatusCode);

        manager.OnWorkerStartStream(1, "worker");
        WorkerPodState started = manager.State;
        Assert.Equal(200, Assert.IsType<Ok>(ManagementApiHandlers.GetWorkerReady(manager)).StatusCode);
        Assert.Same(started, manager.State);
        Assert.Equal(WorkerPodStatus.None, started.PodStatus);

        Assert.IsType<Created>(ManagementApiHandlers.AssignWorker(CreateRequest(), manager));
        Assert.IsType<Ok>(ManagementApiHandlers.GetWorkerReady(manager));
        manager.OnSessionTerminated(1);
        WorkerPodState terminated = manager.State;
        Assert.Equal(503, Assert.IsType<StatusCodeHttpResult>(ManagementApiHandlers.GetWorkerReady(manager)).StatusCode);
        Assert.Same(terminated, manager.State);
    }

    [Fact]
    public void AssignWorker_NullRequestIsInvalidEvenBeforeWorkerReady()
    {
        WorkerPodStateManager manager = CreateManager();
        WorkerPodState initial = manager.State;

        Assert.Equal(new("InvalidBody", "request"), Assert.Single(
            AssertValidation(ManagementApiHandlers.AssignWorker(null, manager))));

        Assert.Same(initial, manager.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AssignWorker_ReturnsAllMissingFieldsWithoutChangingState(bool ready)
    {
        WorkerPodStateManager manager = ready ? CreateReadyManager() : CreateManager();
        WorkerPodState before = manager.State;

        IReadOnlyList<RequestValidationError> errors = AssertValidation(ManagementApiHandlers.AssignWorker(new(), manager));

        Assert.Equal<RequestValidationError>(
            [
                new("Required", "functionAppName"),
                new("Required", "functionGroupName"),
                new("Required", "isAlwaysReady"),
                new("Required", "functionAppDirectory"),
                new("Required", "environment")
            ],
            errors);
        Assert.Same(before, manager.State);
    }

    [Fact]
    public void AssignWorker_ReturnsAllBlankFieldsAndInvalidEnvironmentTogether()
    {
        WorkerPodStateManager manager = CreateReadyManager();
        WorkerPodState before = manager.State;
        WorkerAssignRequest request = new()
        {
            FunctionAppName = " ",
            FunctionGroupName = string.Empty,
            FunctionAppDirectory = "\t",
            Environment = new() { [string.Empty] = "private-value", ["PRIVATE_SETTING"] = null }
        };

        IReadOnlyList<RequestValidationError> errors = AssertValidation(ManagementApiHandlers.AssignWorker(request, manager));

        Assert.Equal<RequestValidationError>(
            [
                new("Required", "functionAppName"),
                new("Required", "functionGroupName"),
                new("Required", "isAlwaysReady"),
                new("Required", "functionAppDirectory"),
                new("InvalidValue", "environment")
            ],
            errors);
        Assert.Same(before, manager.State);
        Assert.IsType<Created>(ManagementApiHandlers.AssignWorker(CreateRequest(), manager));
    }

    [Theory]
    [InlineData("app", null)]
    [InlineData("app", "")]
    [InlineData("app", " \t")]
    [InlineData("group", null)]
    [InlineData("group", "")]
    [InlineData("group", " \t")]
    [InlineData("directory", null)]
    [InlineData("directory", "")]
    [InlineData("directory", " \t")]
    [InlineData("alwaysReady", null)]
    [InlineData("environment", null)]
    [InlineData("environmentKey", "")]
    [InlineData("environmentValue", null)]
    public void AssignWorker_InvalidFieldsDoNotClaimAssignment(string field, string? value)
    {
        WorkerPodStateManager manager = CreateReadyManager();
        WorkerPodState before = manager.State;
        WorkerAssignRequest request = field switch
        {
            "app" => CreateRequest(functionAppName: value),
            "group" => CreateRequest(functionGroupName: value),
            "directory" => CreateRequest(functionAppDirectory: value),
            "alwaysReady" => CreateRequest(isAlwaysReady: null),
            "environment" => new WorkerAssignRequest
            {
                FunctionAppName = "app",
                FunctionGroupName = "group",
                FunctionAppDirectory = "app-directory",
                IsAlwaysReady = false
            },
            "environmentKey" => CreateRequest(environment: new() { [string.Empty] = "private-value" }),
            "environmentValue" => CreateRequest(environment: new() { ["SETTING"] = value }),
            _ => throw new ArgumentException("Unknown field.", nameof(field))
        };

        RequestValidationError detail = Assert.Single(AssertValidation(ManagementApiHandlers.AssignWorker(request, manager)));
        string expectedTarget = field switch
        {
            "app" => "functionAppName",
            "group" => "functionGroupName",
            "directory" => "functionAppDirectory",
            "alwaysReady" => "isAlwaysReady",
            _ => "environment"
        };
        Assert.Equal(expectedTarget, detail.Target);
        Assert.Equal(field is "environmentKey" or "environmentValue" ? "InvalidValue" : "Required", detail.Code);

        Assert.Same(before, manager.State);
        Assert.IsType<Created>(ManagementApiHandlers.AssignWorker(CreateRequest(functionAppName: "other-app"), manager));
        Assert.Equal("other-app", manager.State.FunctionAppName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AssignWorker_ExplicitBooleanAndEmptyEnvironmentAreAccepted(bool isAlwaysReady)
    {
        WorkerPodStateManager manager = CreateReadyManager();
        WorkerPodState before = manager.State;

        IResult result = ManagementApiHandlers.AssignWorker(
            CreateRequest(isAlwaysReady: isAlwaysReady, environment: new()), manager);

        Created created = Assert.IsType<Created>(result);
        Assert.Equal(201, created.StatusCode);
        Assert.Equal("/admin/worker/assignment", created.Location);
        Assert.Equal(before.Revision + 1, manager.State.Revision);
        Assert.Equal(WorkerAssignmentState.Ready, manager.State.AssignmentState);
        Assert.Equal(WorkerPodStatus.ReadyForRequest, manager.State.PodStatus);
        Assert.Equal("app", manager.State.FunctionAppName);
        Assert.Equal("group", manager.State.FunctionGroupName);
        Assert.Equal(isAlwaysReady, manager.State.IsAlwaysReady);
        Assert.Equal(before.SessionId, manager.State.SessionId);
        Assert.Equal(before.WorkerId, manager.State.WorkerId);
        Assert.Equal(WorkerAssignmentState.Unassigned, before.AssignmentState);
        Assert.Null(before.FunctionAppName);
    }

    [Fact]
    public void AssignWorker_EmptyValuesAndNonemptyWhitespaceKeysAreAccepted()
    {
        WorkerPodStateManager manager = CreateReadyManager();

        Assert.IsType<Created>(ManagementApiHandlers.AssignWorker(
            CreateRequest(environment: new() { ["SETTING"] = string.Empty, [" "] = string.Empty }), manager));
    }

    [Fact]
    public void AssignWorker_RecordsEnvironmentWithoutApplyingItToProxyProcess()
    {
        WorkerPodStateManager manager = CreateReadyManager();
        string setting = $"WORKERPROXY_ASSIGNMENT_TEST_{Guid.NewGuid():N}";
        Assert.Null(Environment.GetEnvironmentVariable(setting));

        Assert.IsType<Created>(ManagementApiHandlers.AssignWorker(
            CreateRequest(environment: new() { [setting] = "private-value" }), manager));

        Assert.Null(Environment.GetEnvironmentVariable(setting));
        Assert.Equal(WorkerAssignmentState.Ready, manager.State.AssignmentState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AssignWorker_NotReadyDoesNotReserveIdentity(bool attached)
    {
        WorkerPodStateManager manager = CreateManager();
        if (attached)
        {
            manager.OnWorkerAttached(1);
        }

        WorkerPodState before = manager.State;
        AssertError(ManagementApiHandlers.AssignWorker(CreateRequest(), manager), 503, "WorkerNotReady");
        Assert.Same(before, manager.State);

        if (!attached)
        {
            manager.OnWorkerAttached(1);
        }

        manager.OnWorkerStartStream(1, "worker");
        Assert.IsType<Created>(ManagementApiHandlers.AssignWorker(CreateRequest(functionAppName: "other-app"), manager));
        Assert.Equal("other-app", manager.State.FunctionAppName);
    }

    [Fact]
    public void AssignWorker_CopiesEnvironmentAndReplaysEquivalentIdentityRegardlessOfOrder()
    {
        WorkerPodStateManager manager = CreateReadyManager();
        Dictionary<string, string?> environment = new() { ["SETTING"] = "private-value", ["EMPTY"] = string.Empty };
        WorkerAssignRequest request = CreateRequest(environment: environment);
        Assert.IsType<Created>(ManagementApiHandlers.AssignWorker(request, manager));
        WorkerPodState assigned = manager.State;

        environment["SETTING"] = "changed-value";
        environment["NEW"] = "new-value";
        WorkerAssignRequest equivalent = CreateRequest(environment: new()
        {
            ["EMPTY"] = string.Empty,
            ["SETTING"] = "private-value"
        });

        Assert.IsType<NoContent>(ManagementApiHandlers.AssignWorker(equivalent, manager));
        AssertError(ManagementApiHandlers.AssignWorker(request, manager), 409, "AssignmentConflict");
        Assert.Same(assigned, manager.State);
    }

    [Theory]
    [InlineData("app")]
    [InlineData("group")]
    [InlineData("directory")]
    [InlineData("alwaysReady")]
    [InlineData("environmentKey")]
    [InlineData("environmentValue")]
    public void AssignWorker_DifferentIdentityConflictsBeforeAndAfterTermination(string field)
    {
        WorkerPodStateManager manager = CreateReadyManager();
        Assert.IsType<Created>(ManagementApiHandlers.AssignWorker(CreateRequest(), manager));
        WorkerAssignRequest different = field switch
        {
            "app" => CreateRequest(functionAppName: "APP"),
            "group" => CreateRequest(functionGroupName: "GROUP"),
            "directory" => CreateRequest(functionAppDirectory: "APP-DIRECTORY"),
            "alwaysReady" => CreateRequest(isAlwaysReady: true),
            "environmentKey" => CreateRequest(environment: new() { ["setting"] = "private-value" }),
            "environmentValue" => CreateRequest(environment: new() { ["SETTING"] = "PRIVATE-VALUE" }),
            _ => throw new ArgumentException("Unknown field.", nameof(field))
        };
        WorkerPodState assigned = manager.State;

        AssertError(ManagementApiHandlers.AssignWorker(different, manager), 409, "AssignmentConflict");
        Assert.Same(assigned, manager.State);
        manager.OnSessionTerminated(1);
        WorkerPodState terminated = manager.State;

        AssertError(ManagementApiHandlers.AssignWorker(different, manager), 409, "AssignmentConflict");
        AssertError(ManagementApiHandlers.AssignWorker(CreateRequest(), manager), 409, "WorkerTerminated");
        Assert.Same(terminated, manager.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetInstanceStateAsync_UnspecifiedRevisionReturnsImmediateImmutableSnapshot(bool assigned)
    {
        PollClock clock = new();
        WorkerPodStateManager manager = CreateManager(clock.Provider);
        if (assigned)
        {
            manager.OnWorkerAttached(1);
            manager.OnWorkerStartStream(1, "worker");
            Assert.IsType<Created>(ManagementApiHandlers.AssignWorker(CreateRequest(), manager));
        }

        WorkerPodState before = manager.State;
        Task<IResult> poll = ManagementApiHandlers.GetInstanceStateAsync(null, manager);

        Assert.True(poll.IsCompletedSuccessfully);
        WorkerInstanceState response = AssertState(await poll, before);
        Assert.Same(before, manager.State);
        Assert.Equal(0, manager.PendingWaiterCount);
        Assert.Empty(clock.Timers);

        if (assigned)
        {
            manager.OnSessionTerminated(1);
        }
        else
        {
            manager.OnWorkerAttached(1);
        }

        Assert.Equal(WorkerInstanceState.FromState(before), response);
        Assert.NotEqual(manager.State.Revision, response.RevisionId);
    }

    [Fact]
    public async Task GetInstanceStateAsync_StaleRevisionReturnsCurrentSnapshotImmediately()
    {
        PollClock clock = new();
        WorkerPodStateManager manager = CreateManager(clock.Provider);
        manager.OnWorkerAttached(1);

        Task<IResult> poll = ManagementApiHandlers.GetInstanceStateAsync(0, manager);

        Assert.True(poll.IsCompletedSuccessfully);
        AssertState(await poll, manager.State);
        Assert.Equal(0, manager.PendingWaiterCount);
        Assert.Empty(clock.Timers);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(long.MaxValue)]
    public async Task GetInstanceStateAsync_InvalidRevisionReturnsErrorWithoutWaiting(long revision)
    {
        PollClock clock = new();
        WorkerPodStateManager manager = CreateManager(clock.Provider);
        WorkerPodState initial = manager.State;

        Assert.Equal(new("InvalidRevision", "lastKnownRevision"), Assert.Single(
            AssertValidation(await ManagementApiHandlers.GetInstanceStateAsync(
                revision, manager))));

        Assert.Same(initial, manager.State);
        Assert.Equal(0, manager.PendingWaiterCount);
        Assert.Empty(clock.Timers);
    }

    [Theory]
    [InlineData("attach")]
    [InlineData("start")]
    [InlineData("assign")]
    [InlineData("terminate")]
    public async Task GetInstanceStateAsync_EqualRevisionWaitsForTypedChangedSnapshot(string transition)
    {
        PollClock clock = new();
        WorkerPodStateManager manager = CreateManager(clock.Provider);
        if (!string.Equals(transition, "attach", StringComparison.Ordinal))
        {
            manager.OnWorkerAttached(1);
        }

        if (transition is "assign" or "terminate")
        {
            manager.OnWorkerStartStream(1, "worker");
        }

        if (string.Equals(transition, "terminate", StringComparison.Ordinal))
        {
            Assert.IsType<Created>(ManagementApiHandlers.AssignWorker(CreateRequest(), manager));
        }

        long revision = manager.State.Revision;
        Task<IResult> poll = ManagementApiHandlers.GetInstanceStateAsync(revision, manager);
        Assert.False(poll.IsCompleted);
        Assert.Equal(1, manager.PendingWaiterCount);

        switch (transition)
        {
            case "attach":
                manager.OnWorkerAttached(1);
                break;
            case "start":
                manager.OnWorkerStartStream(1, "worker");
                break;
            case "assign":
                Assert.IsType<Created>(ManagementApiHandlers.AssignWorker(CreateRequest(), manager));
                break;
            case "terminate":
                manager.OnSessionTerminated(1);
                break;
            default:
                throw new ArgumentException("Unknown transition.", nameof(transition));
        }

        WorkerInstanceState response = AssertState(await poll.WaitAsync(TestTimeout), manager.State);
        Assert.Equal(revision + 1, response.RevisionId);
        Assert.Equal(0, manager.PendingWaiterCount);
        clock.VerifyTimersDisposed();
    }

    [Fact]
    public async Task GetInstanceStateAsync_ReplaysAndConflictsDoNotWakePollBeforeSixtySecondTimeout()
    {
        PollClock clock = new();
        WorkerPodStateManager manager = CreateReadyManager(clock.Provider);
        Assert.IsType<Created>(ManagementApiHandlers.AssignWorker(CreateRequest(), manager));
        WorkerPodState assigned = manager.State;
        Task<IResult> poll = ManagementApiHandlers.GetInstanceStateAsync(
            assigned.Revision, manager);

        Assert.IsType<NoContent>(ManagementApiHandlers.AssignWorker(CreateRequest(), manager));
        AssertError(ManagementApiHandlers.AssignWorker(CreateRequest(functionAppName: "other"), manager),
            409, "AssignmentConflict");
        Assert.False(poll.IsCompleted);
        Assert.Equal(1, manager.PendingWaiterCount);
        PollClock.ScheduledTimer timer = Assert.Single(clock.Timers);
        Assert.Equal(TimeSpan.FromSeconds(60), timer.DueTime);
        Assert.Equal(Timeout.InfiniteTimeSpan, timer.Period);

        timer.Fire();

        Assert.Equal(204, Assert.IsType<NoContent>(await poll.WaitAsync(TestTimeout)).StatusCode);
        Assert.Same(assigned, manager.State);
        Assert.Equal(0, manager.PendingWaiterCount);
        clock.VerifyTimersDisposed();
    }

    [Fact]
    public async Task GetInstanceStateAsync_CancellationPropagatesAndLeavesOtherPollActive()
    {
        PollClock clock = new();
        WorkerPodStateManager manager = CreateManager(clock.Provider);
        using CancellationTokenSource cancellation = new();
        Task<IResult> canceledPoll = ManagementApiHandlers.GetInstanceStateAsync(
            0, manager, cancellation.Token);
        Task<IResult> activePoll = ManagementApiHandlers.GetInstanceStateAsync(
            0, manager);
        Assert.Equal(2, manager.PendingWaiterCount);

        cancellation.Cancel();
        OperationCanceledException exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => canceledPoll.WaitAsync(TestTimeout));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(1, manager.PendingWaiterCount);
        Assert.False(activePoll.IsCompleted);
        manager.OnWorkerAttached(1);
        AssertState(await activePoll.WaitAsync(TestTimeout), manager.State);
        Assert.Equal(0, manager.PendingWaiterCount);
        clock.VerifyTimersDisposed();
    }

    [Fact]
    public async Task GetInstanceStateAsync_CancelingOnlyPollRemovesWaiterWithoutChangingState()
    {
        PollClock clock = new();
        WorkerPodStateManager manager = CreateManager(clock.Provider);
        WorkerPodState initial = manager.State;
        using CancellationTokenSource cancellation = new();
        Task<IResult> poll = ManagementApiHandlers.GetInstanceStateAsync(
            0, manager, cancellation.Token);
        Assert.Equal(1, manager.PendingWaiterCount);

        cancellation.Cancel();
        OperationCanceledException exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => poll.WaitAsync(TestTimeout));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Same(initial, manager.State);
        Assert.Equal(0, manager.PendingWaiterCount);
        clock.VerifyTimersDisposed();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    public async Task GetInstanceStateAsync_PreCanceledRequestPropagatesWithoutRegisteringWaiter(long? revision)
    {
        PollClock clock = new();
        WorkerPodStateManager manager = CreateManager(clock.Provider);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        OperationCanceledException exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ManagementApiHandlers.GetInstanceStateAsync(
                revision, manager, cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(0, manager.PendingWaiterCount);
        Assert.Empty(clock.Timers);
    }

    private static WorkerPodStateManager CreateManager(TimeProvider? timeProvider = null) =>
        new(Options.Create(new WorkerProxyOptions { PodName = "pod" }), timeProvider ?? TimeProvider.System);

    private static WorkerPodStateManager CreateReadyManager(TimeProvider? timeProvider = null)
    {
        WorkerPodStateManager manager = CreateManager(timeProvider);
        manager.OnWorkerAttached(1);
        manager.OnWorkerStartStream(1, "worker");
        return manager;
    }

    private static WorkerAssignRequest CreateRequest(
        string? functionAppName = "app",
        string? functionGroupName = "group",
        bool? isAlwaysReady = false,
        string? functionAppDirectory = "app-directory",
        Dictionary<string, string?>? environment = null) =>
        new()
        {
            FunctionAppName = functionAppName,
            FunctionGroupName = functionGroupName,
            IsAlwaysReady = isAlwaysReady,
            FunctionAppDirectory = functionAppDirectory,
            Environment = environment ?? new() { ["SETTING"] = "private-value" }
        };

    private static WorkerApiError AssertError(IResult result, int statusCode, string code)
    {
        JsonHttpResult<WorkerApiErrorResponse> json = Assert.IsType<JsonHttpResult<WorkerApiErrorResponse>>(result);
        Assert.Equal(statusCode, json.StatusCode);
        WorkerApiErrorResponse response = Assert.IsType<WorkerApiErrorResponse>(json.Value);
        Assert.Equal(code, response.Error.Code);
        Assert.False(string.IsNullOrWhiteSpace(response.Error.Detail));
        Assert.DoesNotContain("private-value", response.Error.Detail!);
        Assert.DoesNotContain("app-directory", response.Error.Detail!);
        return response.Error;
    }

    private static IReadOnlyList<RequestValidationError> AssertValidation(IResult result)
    {
        JsonHttpResult<RequestValidationResponse> json = Assert.IsType<JsonHttpResult<RequestValidationResponse>>(result);
        Assert.Equal(400, json.StatusCode);
        RequestValidationResponse response = Assert.IsType<RequestValidationResponse>(json.Value);
        Assert.NotEmpty(response.Errors);
        return response.Errors;
    }

    private static WorkerInstanceState AssertState(IResult result, WorkerPodState expected)
    {
        JsonHttpResult<WorkerInstanceState> json = Assert.IsType<JsonHttpResult<WorkerInstanceState>>(result);
        Assert.Equal(200, json.StatusCode ?? StatusCodes.Status200OK);
        WorkerInstanceState response = Assert.IsType<WorkerInstanceState>(json.Value);
        Assert.Equal(WorkerInstanceState.FromState(expected), response);
        return response;
    }

    private sealed class PollClock
    {
        public PollClock()
        {
            Mock<TimeProvider> provider = new();
            provider.Setup(clock => clock.CreateTimer(
                It.IsAny<TimerCallback>(), It.IsAny<object?>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
                .Returns((TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
                {
                    ScheduledTimer timer = new(callback, state, dueTime, period);
                    Timers.Add(timer);
                    return timer.Timer.Object;
                });
            Provider = provider.Object;
        }

        public TimeProvider Provider { get; }

        public List<ScheduledTimer> Timers { get; } = [];

        public void VerifyTimersDisposed() =>
            Assert.All(Timers, timer => timer.Timer.Verify(instance => instance.Dispose(), Times.AtLeastOnce()));

        public sealed record ScheduledTimer(TimerCallback Callback, object? State, TimeSpan DueTime, TimeSpan Period)
        {
            public Mock<ITimer> Timer { get; } = new();

            public void Fire() => Callback(State);
        }
    }
}
