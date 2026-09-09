// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Azure.Functions.Host.Controllers;
using Azure.Functions.Host.Models;
using Azure.Functions.Host.WorkerLink;
using Azure.Functions.Rpc.Client;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using GrpcException = Grpc.Core.RpcException;
using WorkerRpcException = Microsoft.Azure.WebJobs.Script.Workers.Rpc.RpcException;

namespace Azure.Functions.Host.Tests;

public class WorkerLinkControllerTests
{
    private const string WorkerId = "worker-pod-abc123";
    private const string ValidGrpcEndpoint = "http://100.64.1.12:50053";
    private const string PrivateDiagnostic = "private-worker-initialization-diagnostic";
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);
    private readonly Mock<IWorkerChannelRegistry> _registry = new(MockBehavior.Strict);

    [Fact]
    public void Constructor_NullLoggerOrRegistry_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new WorkerLinkController(null!, _registry.Object));
        Assert.Throws<ArgumentNullException>(() => new WorkerLinkController(NullLogger<WorkerLinkController>.Instance, null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-2)]
    public void Constructor_InvalidTimeout_Throws(int milliseconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateController(TimeSpan.FromMilliseconds(milliseconds)));
        _registry.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("http://100.64.1.12:50053", null)]
    [InlineData("http://100.64.1.12:50053", "http://100.64.1.12:48801")]
    [InlineData("https://100.64.1.12:50053", "https://100.64.1.12:48801")]
    public async Task LinkWorker_ValidRequest_AwaitsLinkAndReturnsLinked(string grpcEndpoint, string? httpEndpoint)
    {
        using CancellationTokenSource cancellation = new();
        TaskCompletionSource<WorkerChannel> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken registryToken = default;
        _registry.Setup(registry => registry.LinkAsync(WorkerId, new Uri(grpcEndpoint), It.IsAny<CancellationToken>()))
            .Callback<string, Uri, CancellationToken>((_, _, token) => registryToken = token)
            .Returns(completion.Task);
        WorkerLinkRequest request = ValidRequest();
        request.WorkerGrpcEndpoint = grpcEndpoint;
        request.WorkerHttpEndpoint = httpEndpoint;
        request.WorkerContainerEncryptionKey = "reserved-test-value";

        Task<IActionResult> link = CreateController().LinkWorker(request, cancellation.Token);
        Assert.True(registryToken.CanBeCanceled);
        Assert.NotEqual(cancellation.Token, registryToken);
        Assert.False(registryToken.IsCancellationRequested);
        Assert.False(link.IsCompleted);
        completion.SetResult(null!);
        var result = Assert.IsType<OkObjectResult>(await link);
        var response = Assert.IsType<WorkerLinkResponse>(result.Value);
        Assert.Equal(WorkerId, response.WorkerPodName);
        Assert.Equal(WorkerLinkStatus.Linked, response.Status);
        Assert.Null(response.Detail);
        VerifyOnlyLinkCall();
    }

    [Theory]
    [InlineData(WorkerLinkFailureReason.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(WorkerLinkFailureReason.WorkerTerminated, StatusCodes.Status409Conflict)]
    [InlineData(WorkerLinkFailureReason.RuntimeStopping, StatusCodes.Status409Conflict)]
    [InlineData(WorkerLinkFailureReason.Unavailable, StatusCodes.Status503ServiceUnavailable)]
    public async Task LinkWorker_ExpectedFailure_ReturnsLinkFailed(WorkerLinkFailureReason reason, int status)
    {
        _registry.Setup(registry => registry.LinkAsync(WorkerId, new Uri(ValidGrpcEndpoint), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WorkerLinkException(reason, "Safe failure detail.", new Exception("Internal diagnostics.")));

        var result = Assert.IsType<ObjectResult>(await CreateController().LinkWorker(ValidRequest()));
        Assert.Equal(status, result.StatusCode);
        var response = Assert.IsType<WorkerLinkResponse>(result.Value);
        Assert.Equal(WorkerId, response.WorkerPodName);
        Assert.Equal(WorkerLinkStatus.LinkFailed, response.Status);
        Assert.Equal("Safe failure detail.", response.Detail);
        VerifyOnlyLinkCall();
    }

    [Fact]
    public async Task LinkWorker_PreCanceledRequest_DoesNotLink()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        OperationCanceledException actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateController().LinkWorker(ValidRequest(), cancellation.Token));
        Assert.Equal(cancellation.Token, actual.CancellationToken);
        _registry.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LinkWorker_CallerCancellation_CancelsRegistryAndPropagatesCallerToken()
    {
        using CancellationTokenSource cancellation = new();
        TaskCompletionSource<WorkerChannel> initialization = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken registryToken = default;
        _registry.Setup(registry => registry.LinkAsync(WorkerId, new Uri(ValidGrpcEndpoint), It.IsAny<CancellationToken>()))
            .Returns((string _, Uri _, CancellationToken token) =>
            {
                registryToken = token;
                return initialization.Task.WaitAsync(token);
            });

        Task<IActionResult> link = CreateController().LinkWorker(ValidRequest(), cancellation.Token);
        Assert.False(link.IsCompleted);
        cancellation.Cancel();

        OperationCanceledException actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => link.WaitAsync(TestTimeout));
        Assert.True(registryToken.IsCancellationRequested);
        Assert.Equal(cancellation.Token, actual.CancellationToken);
        VerifyOnlyLinkCall();
    }

    [Fact]
    public async Task LinkWorker_Deadline_CancelsRegistryAndReturnsUnavailable()
    {
        using CancellationTokenSource cancellation = new();
        TaskCompletionSource<WorkerChannel> initialization = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken registryToken = default;
        _registry.Setup(registry => registry.LinkAsync(WorkerId, new Uri(ValidGrpcEndpoint), It.IsAny<CancellationToken>()))
            .Returns((string _, Uri _, CancellationToken token) =>
            {
                registryToken = token;
                return initialization.Task.WaitAsync(token);
            });
        WorkerLinkController controller = CreateController(TimeSpan.FromMilliseconds(100));

        IActionResult result = await controller.LinkWorker(ValidRequest(), cancellation.Token).WaitAsync(TestTimeout);

        AssertLinkFailed(result, StatusCodes.Status503ServiceUnavailable);
        Assert.True(registryToken.IsCancellationRequested);
        Assert.False(cancellation.IsCancellationRequested);
        VerifyOnlyLinkCall();
    }

    [Fact]
    public async Task LinkWorker_CompletedReplay_CompletesSynchronously()
    {
        _registry.Setup(registry => registry.LinkAsync(WorkerId, new Uri(ValidGrpcEndpoint), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkerChannel)null!);

        Task<IActionResult> link = CreateController().LinkWorker(ValidRequest());

        Assert.True(link.IsCompletedSuccessfully);
        Assert.IsType<OkObjectResult>(await link);
        VerifyOnlyLinkCall();
    }

    [Fact]
    public async Task LinkWorker_SynchronousRejection_ReturnsConflict()
    {
        _registry.Setup(registry => registry.LinkAsync(WorkerId, new Uri(ValidGrpcEndpoint), It.IsAny<CancellationToken>()))
            .Throws(new WorkerLinkException(WorkerLinkFailureReason.Conflict, "The worker endpoint conflicts."));

        Task<IActionResult> link = CreateController().LinkWorker(ValidRequest());

        Assert.True(link.IsCompletedSuccessfully);
        AssertLinkFailed(await link, StatusCodes.Status409Conflict);
        VerifyOnlyLinkCall();
    }

    [Fact]
    public async Task LinkWorker_EachRequestDelegatesAdmissionToRegistry()
    {
        TaskCompletionSource<WorkerChannel> initialization = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _registry.Setup(registry => registry.LinkAsync(It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .Returns(initialization.Task);
        WorkerLinkController controller = CreateController();
        WorkerLinkRequest otherRequest = new() { WorkerPodName = "other-worker", WorkerGrpcEndpoint = ValidGrpcEndpoint };

        Task<IActionResult> first = controller.LinkWorker(ValidRequest());
        Task<IActionResult> replay = controller.LinkWorker(ValidRequest());
        Task<IActionResult> other = controller.LinkWorker(otherRequest);

        Assert.False(first.IsCompleted);
        Assert.False(replay.IsCompleted);
        Assert.False(other.IsCompleted);
        initialization.SetResult(null!);
        IActionResult[] results = await Task.WhenAll(first, replay, other).WaitAsync(TestTimeout);
        Assert.All(results, result => Assert.IsType<OkObjectResult>(result));
        _registry.Verify(registry => registry.LinkAsync(WorkerId, new Uri(ValidGrpcEndpoint), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _registry.Verify(registry => registry.LinkAsync("other-worker", new Uri(ValidGrpcEndpoint), It.IsAny<CancellationToken>()), Times.Once);
        _registry.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LinkWorker_RegistryStopping_ReturnsSafeConflict()
    {
        _registry.Setup(registry => registry.LinkAsync(WorkerId, new Uri(ValidGrpcEndpoint), It.IsAny<CancellationToken>()))
            .Throws(new ObjectDisposedException(PrivateDiagnostic));

        IActionResult result = await CreateController().LinkWorker(ValidRequest());

        WorkerLinkResponse response = AssertLinkFailed(result, StatusCodes.Status409Conflict);
        Assert.Equal("The runtime is stopping.", response.Detail);
        VerifyOnlyLinkCall();
    }

    [Theory]
    [InlineData(typeof(GrpcException))]
    [InlineData(typeof(WorkerRpcException))]
    [InlineData(typeof(HttpRequestException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(SocketException))]
    [InlineData(typeof(TimeoutException))]
    [InlineData(typeof(ChannelClosedException))]
    [InlineData(typeof(OperationCanceledException))]
    [InlineData(typeof(UriFormatException))]
    public async Task LinkWorker_DependencyFailure_ReturnsSafeUnavailable(Type exceptionType)
    {
        Exception failure = exceptionType switch
        {
            Type type when type == typeof(GrpcException) => new GrpcException(new Status(StatusCode.Unavailable, PrivateDiagnostic)),
            Type type when type == typeof(WorkerRpcException) => new WorkerRpcException("Failure", PrivateDiagnostic, "Remote stack."),
            Type type when type == typeof(HttpRequestException) => new HttpRequestException(PrivateDiagnostic),
            Type type when type == typeof(IOException) => new IOException(PrivateDiagnostic),
            Type type when type == typeof(SocketException) => new SocketException((int)SocketError.ConnectionRefused),
            Type type when type == typeof(TimeoutException) => new TimeoutException(PrivateDiagnostic),
            Type type when type == typeof(ChannelClosedException) => new ChannelClosedException(PrivateDiagnostic),
            Type type when type == typeof(OperationCanceledException) => new OperationCanceledException(PrivateDiagnostic),
            Type type when type == typeof(UriFormatException) => new UriFormatException(PrivateDiagnostic),
            _ => throw new ArgumentOutOfRangeException(nameof(exceptionType)),
        };
        _registry.Setup(registry => registry.LinkAsync(WorkerId, new Uri(ValidGrpcEndpoint), It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);

        IActionResult result = await CreateController().LinkWorker(ValidRequest()).WaitAsync(TestTimeout);

        WorkerLinkResponse response = AssertLinkFailed(result, StatusCodes.Status503ServiceUnavailable);
        Assert.Equal("The worker connection or initialization handshake was unavailable.", response.Detail);
        Assert.DoesNotContain(failure.Message, response.Detail, StringComparison.Ordinal);
        VerifyOnlyLinkCall();
    }

    [Fact]
    public async Task LinkWorker_UnexpectedFailure_IsNotReportedAsUnavailable()
    {
        InvalidOperationException failure = new("Programming error.");
        _registry.Setup(registry => registry.LinkAsync(WorkerId, new Uri(ValidGrpcEndpoint), It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateController().LinkWorker(ValidRequest()));
        Assert.Same(failure, actual);
        VerifyOnlyLinkCall();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LinkWorker_LogsStructuredOutcomeWithoutReservedKey(bool fail)
    {
        Mock<ILogger<WorkerLinkController>> logger = new();
        logger.Setup(value => value.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        Dictionary<string, object>? state = null;
        EventId eventId = default;
        string? rendered = null;
        logger.Setup(value => value.Log(
            It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback(new InvocationAction(call =>
            {
                // The generated logger reuses its state after Log returns.
                state = Assert.IsAssignableFrom<IEnumerable<KeyValuePair<string, object>>>(call.Arguments[2])
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                eventId = (EventId)call.Arguments[1];
                rendered = call.Arguments[2].ToString();
            }));
        _registry.Setup(registry => registry.LinkAsync(WorkerId, new Uri(ValidGrpcEndpoint), It.IsAny<CancellationToken>()))
            .Returns(fail
                ? Task.FromException<WorkerChannel>(new WorkerLinkException(WorkerLinkFailureReason.Conflict, "Conflict."))
                : Task.FromResult<WorkerChannel>(null!));
        WorkerLinkRequest request = ValidRequest();
        request.WorkerContainerEncryptionKey = "reserved-value-do-not-log";
        WorkerLinkController controller = new(logger.Object, _registry.Object);

        await controller.LinkWorker(request);

        Assert.Single(logger.Invocations.Where(call => string.Equals(call.Method.Name, nameof(ILogger.Log), StringComparison.Ordinal)));
        Assert.Equal(fail ? 1 : 0, eventId.Id);
        Dictionary<string, object> loggedState = Assert.IsType<Dictionary<string, object>>(state);
        Assert.Equal(WorkerId, loggedState["workerId"]);
        Assert.True(Assert.IsType<double>(loggedState["elapsedMilliseconds"]) >= 0);
        Assert.DoesNotContain(request.WorkerContainerEncryptionKey, rendered, StringComparison.Ordinal);
        if (fail)
        {
            Assert.Equal(nameof(WorkerLinkFailureReason.Conflict), loggedState["reason"]);
        }
    }

    [Fact]
    public async Task LinkWorker_NullRequest_ReturnsBadRequestWithoutLinking()
    {
        IReadOnlyList<RequestValidationError> errors = AssertBadRequest(await CreateController().LinkWorker(null));
        RequestValidationError error = Assert.Single(errors);
        Assert.Equal("InvalidBody", error.Code);
        Assert.Equal("request", error.Target);
        _registry.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LinkWorker_InvalidModelState_ReturnsBadRequestWithoutLinking()
    {
        WorkerLinkController controller = CreateController();
        controller.ModelState.AddModelError("request", "Invalid JSON containing a private value.");
        IReadOnlyList<RequestValidationError> errors = AssertBadRequest(await controller.LinkWorker(ValidRequest()));
        RequestValidationError error = Assert.Single(errors);
        Assert.Equal("InvalidBody", error.Code);
        Assert.Equal("request", error.Target);
        _registry.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LinkWorker_BothRequiredFieldsMissing_ReturnsBothErrorsWithoutLinking()
    {
        IReadOnlyList<RequestValidationError> errors = AssertBadRequest(await CreateController().LinkWorker(new WorkerLinkRequest()));
        Assert.Equal(
            [("workerPodName", "Required"), ("workerGrpcEndpoint", "Required")],
            errors.Select(error => (error.Target, error.Code)));
        _registry.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LinkWorker_MultipleInvalidFields_ReturnsAllErrorsWithoutLinking()
    {
        WorkerLinkRequest request = new()
        {
            WorkerPodName = " ",
            WorkerGrpcEndpoint = "relative/grpc",
            WorkerHttpEndpoint = "relative/http",
        };
        IReadOnlyList<RequestValidationError> errors = AssertBadRequest(await CreateController().LinkWorker(request));
        Assert.Equal(
            [("workerPodName", "Required"), ("workerGrpcEndpoint", "InvalidEndpoint"), ("workerHttpEndpoint", "InvalidEndpoint")],
            errors.Select(error => (error.Target, error.Code)));
        _registry.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task LinkWorker_MissingWorkerPodName_ReturnsBadRequest(string? workerPodName)
    {
        WorkerLinkRequest request = ValidRequest();
        request.WorkerPodName = workerPodName;
        IReadOnlyList<RequestValidationError> errors = AssertBadRequest(await CreateController().LinkWorker(request));
        RequestValidationError error = Assert.Single(errors);
        Assert.Equal("Required", error.Code);
        Assert.Equal("workerPodName", error.Target);
        _registry.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-url")]
    [InlineData("worker-proxy:50053")]
    [InlineData("ftp://100.64.1.12:50053")]
    [InlineData("http://user:password@100.64.1.12:50053")]
    [InlineData("http://100.64.1.12:50053/path")]
    [InlineData("http://100.64.1.12:50053?query=value")]
    [InlineData("http://100.64.1.12:50053#fragment")]
    public async Task LinkWorker_InvalidGrpcEndpoint_ReturnsBadRequest(string? grpcEndpoint)
    {
        WorkerLinkRequest request = ValidRequest();
        request.WorkerGrpcEndpoint = grpcEndpoint;
        IReadOnlyList<RequestValidationError> errors = AssertBadRequest(await CreateController().LinkWorker(request));
        RequestValidationError error = Assert.Single(errors);
        Assert.Equal(string.IsNullOrWhiteSpace(grpcEndpoint) ? "Required" : "InvalidEndpoint", error.Code);
        Assert.Equal("workerGrpcEndpoint", error.Target);
        _registry.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://100.64.1.12:48801")]
    [InlineData("http://100.64.1.12:48801/path")]
    public async Task LinkWorker_InvalidHttpEndpoint_ReturnsBadRequest(string httpEndpoint)
    {
        WorkerLinkRequest request = ValidRequest();
        request.WorkerHttpEndpoint = httpEndpoint;
        IReadOnlyList<RequestValidationError> errors = AssertBadRequest(await CreateController().LinkWorker(request));
        RequestValidationError error = Assert.Single(errors);
        Assert.Equal("InvalidEndpoint", error.Code);
        Assert.Equal("workerHttpEndpoint", error.Target);
        _registry.VerifyNoOtherCalls();
    }

    private WorkerLinkController CreateController(TimeSpan? linkTimeout = null)
        => linkTimeout.HasValue
            ? new(NullLogger<WorkerLinkController>.Instance, _registry.Object, linkTimeout.Value)
            : new(NullLogger<WorkerLinkController>.Instance, _registry.Object);

    private void VerifyOnlyLinkCall()
    {
        _registry.Verify(registry => registry.LinkAsync(WorkerId, It.IsAny<Uri>(), It.IsAny<CancellationToken>()), Times.Once);
        _registry.VerifyNoOtherCalls();
    }

    private static WorkerLinkResponse AssertLinkFailed(IActionResult result, int statusCode)
    {
        ObjectResult failure = Assert.IsType<ObjectResult>(result);
        Assert.Equal(statusCode, failure.StatusCode);
        WorkerLinkResponse response = Assert.IsType<WorkerLinkResponse>(failure.Value);
        Assert.Equal(WorkerId, response.WorkerPodName);
        Assert.Equal(WorkerLinkStatus.LinkFailed, response.Status);
        Assert.False(string.IsNullOrWhiteSpace(response.Detail));
        Assert.DoesNotContain(PrivateDiagnostic, response.Detail, StringComparison.Ordinal);
        return response;
    }

    private static WorkerLinkRequest ValidRequest()
        => new() { WorkerPodName = WorkerId, WorkerGrpcEndpoint = ValidGrpcEndpoint };

    private static IReadOnlyList<RequestValidationError> AssertBadRequest(IActionResult result)
    {
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
        RequestValidationResponse response = Assert.IsType<RequestValidationResponse>(badRequest.Value);
        IReadOnlyList<RequestValidationError> errors = response.Errors;
        Assert.NotEmpty(errors);
        Assert.All(errors, error =>
        {
            Assert.False(string.IsNullOrWhiteSpace(error.Target));
            Assert.Contains(error.Code, new[] { "Required", "InvalidEndpoint", "InvalidBody" });
        });

        return errors;
    }
}
