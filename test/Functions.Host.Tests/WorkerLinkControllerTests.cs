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
    [InlineData("http://100.64.1.12:50053", null)]
    [InlineData("http://100.64.1.12:50053", "")]
    [InlineData("http://100.64.1.12:50053", " \t ")]
    [InlineData("http://100.64.1.12:50053", "http://100.64.1.12:48801")]
    [InlineData("https://100.64.1.12:50053", "https://100.64.1.12:48801")]
    public async Task LinkWorker_ValidRequest_AwaitsLinkAndReturnsCreated(string grpcEndpoint, string? httpEndpoint)
    {
        using CancellationTokenSource cancellation = new();
        TaskCompletionSource<WorkerLinkResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken registryToken = default;
        Uri? expectedHttpEndpoint = string.IsNullOrWhiteSpace(httpEndpoint) ? null : new Uri(httpEndpoint);
        _registry.Setup(registry => registry.LinkAsync(WorkerId, new Uri(grpcEndpoint), expectedHttpEndpoint, It.IsAny<CancellationToken>()))
            .Callback<string, Uri, Uri?, CancellationToken>((_, _, _, token) => registryToken = token)
            .Returns(completion.Task);
        WorkerLinkRequest request = ValidRequest();
        request.WorkerGrpcEndpoint = grpcEndpoint;
        request.WorkerHttpEndpoint = httpEndpoint;
        request.WorkerContainerEncryptionKey = "reserved-test-value";

        Task<IActionResult> link = CreateController().LinkWorker(WorkerId, request, cancellation.Token);
        Assert.True(registryToken.CanBeCanceled);
        Assert.Equal(cancellation.Token, registryToken);
        Assert.False(registryToken.IsCancellationRequested);
        Assert.False(link.IsCompleted);
        completion.SetResult(new(null!, IsNewLink: true));
        StatusCodeResult result = Assert.IsType<StatusCodeResult>(await link);
        Assert.Equal(StatusCodes.Status201Created, result.StatusCode);
        VerifyOnlyLinkCall(expectedHttpEndpoint);
    }

    [Theory]
    [InlineData(WorkerLinkFailureReason.Conflict, StatusCodes.Status409Conflict, "LinkConflict")]
    [InlineData(WorkerLinkFailureReason.WorkerTerminated, StatusCodes.Status409Conflict, "WorkerTerminated")]
    [InlineData(WorkerLinkFailureReason.RuntimeStopping, StatusCodes.Status503ServiceUnavailable, "RuntimeStopping")]
    [InlineData(WorkerLinkFailureReason.Unavailable, StatusCodes.Status503ServiceUnavailable, "WorkerUnavailable")]
    [InlineData(WorkerLinkFailureReason.Timeout, StatusCodes.Status503ServiceUnavailable, "LinkTimeout")]
    public async Task LinkWorker_ExpectedFailure_ReturnsStableError(WorkerLinkFailureReason reason, int status, string code)
    {
        _registry.Setup(registry => registry.LinkAsync(WorkerId, new Uri(ValidGrpcEndpoint), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WorkerLinkException(reason, PrivateDiagnostic, new Exception("Internal diagnostics.")));

        IActionResult result = await CreateController().LinkWorker(WorkerId, ValidRequest());
        AssertLinkError(result, status, code);
        VerifyOnlyLinkCall();
    }

    [Fact]
    public async Task LinkWorker_PreCanceledRequest_DoesNotLink()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        OperationCanceledException actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateController().LinkWorker(WorkerId, ValidRequest(), cancellation.Token));
        Assert.Equal(cancellation.Token, actual.CancellationToken);
        _registry.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LinkWorker_CallerCancellation_CancelsRegistryAndPropagatesCallerToken()
    {
        using CancellationTokenSource cancellation = new();
        TaskCompletionSource<WorkerLinkResult> initialization = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken registryToken = default;
        _registry.Setup(registry => registry.LinkAsync(WorkerId, new Uri(ValidGrpcEndpoint), null, It.IsAny<CancellationToken>()))
            .Returns((string _, Uri _, Uri? _, CancellationToken token) =>
            {
                registryToken = token;
                return initialization.Task.WaitAsync(token);
            });

        Task<IActionResult> link = CreateController().LinkWorker(WorkerId, ValidRequest(), cancellation.Token);
        Assert.False(link.IsCompleted);
        cancellation.Cancel();

        OperationCanceledException actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => link.WaitAsync(TestTimeout));
        Assert.True(registryToken.IsCancellationRequested);
        Assert.Equal(cancellation.Token, actual.CancellationToken);
        VerifyOnlyLinkCall();
    }

    [Fact]
    public async Task LinkWorker_RegistryDeadline_ReturnsLinkTimeoutWithoutCancelingRequest()
    {
        using CancellationTokenSource cancellation = new();
        _registry.Setup(registry => registry.LinkAsync(WorkerId, new Uri(ValidGrpcEndpoint), null, cancellation.Token))
            .ThrowsAsync(new TimeoutException(PrivateDiagnostic));

        IActionResult result = await CreateController().LinkWorker(WorkerId, ValidRequest(), cancellation.Token).WaitAsync(TestTimeout);

        AssertLinkError(result, StatusCodes.Status503ServiceUnavailable, "LinkTimeout");
        Assert.False(cancellation.IsCancellationRequested);
        VerifyOnlyLinkCall();
    }

    [Theory]
    [InlineData(true, StatusCodes.Status201Created)]
    [InlineData(false, StatusCodes.Status200OK)]
    public async Task LinkWorker_CompletedLink_UsesRegistryCreationResult(bool isNewLink, int expectedStatus)
    {
        _registry.Setup(registry => registry.LinkAsync(WorkerId, new Uri(ValidGrpcEndpoint), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkerLinkResult(null!, isNewLink));

        Task<IActionResult> link = CreateController().LinkWorker(WorkerId, ValidRequest());

        Assert.True(link.IsCompletedSuccessfully);
        StatusCodeResult result = Assert.IsType<StatusCodeResult>(await link);
        Assert.Equal(expectedStatus, result.StatusCode);
        VerifyOnlyLinkCall();
    }

    [Fact]
    public async Task LinkWorker_SynchronousRejection_ReturnsConflict()
    {
        _registry.Setup(registry => registry.LinkAsync(WorkerId, new Uri(ValidGrpcEndpoint), null, It.IsAny<CancellationToken>()))
            .Throws(new WorkerLinkException(WorkerLinkFailureReason.Conflict, "The worker endpoint conflicts."));

        Task<IActionResult> link = CreateController().LinkWorker(WorkerId, ValidRequest());

        Assert.True(link.IsCompletedSuccessfully);
        AssertLinkError(await link, StatusCodes.Status409Conflict, "LinkConflict");
        VerifyOnlyLinkCall();
    }

    [Fact]
    public async Task LinkWorker_EachRequestDelegatesAdmissionToRegistry()
    {
        TaskCompletionSource<WorkerLinkResult> initialization = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _registry.Setup(registry => registry.LinkAsync(It.IsAny<string>(), It.IsAny<Uri>(), null, It.IsAny<CancellationToken>()))
            .Returns(initialization.Task);
        WorkerLinkController controller = CreateController();
        WorkerLinkRequest otherRequest = new() { WorkerGrpcEndpoint = ValidGrpcEndpoint };

        Task<IActionResult> first = controller.LinkWorker(WorkerId, ValidRequest());
        Task<IActionResult> replay = controller.LinkWorker(WorkerId, ValidRequest());
        Task<IActionResult> other = controller.LinkWorker("other-worker", otherRequest);

        Assert.False(first.IsCompleted);
        Assert.False(replay.IsCompleted);
        Assert.False(other.IsCompleted);
        initialization.SetResult(new(null!, IsNewLink: false));
        IActionResult[] results = await Task.WhenAll(first, replay, other).WaitAsync(TestTimeout);
        Assert.All(results, result => Assert.Equal(StatusCodes.Status200OK, Assert.IsType<StatusCodeResult>(result).StatusCode));
        _registry.Verify(registry => registry.LinkAsync(WorkerId, new Uri(ValidGrpcEndpoint), null, It.IsAny<CancellationToken>()), Times.Exactly(2));
        _registry.Verify(registry => registry.LinkAsync("other-worker", new Uri(ValidGrpcEndpoint), null, It.IsAny<CancellationToken>()), Times.Once);
        _registry.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LinkWorker_RegistryStopping_ReturnsSafeUnavailable()
    {
        _registry.Setup(registry => registry.LinkAsync(WorkerId, new Uri(ValidGrpcEndpoint), null, It.IsAny<CancellationToken>()))
            .Throws(new ObjectDisposedException(PrivateDiagnostic));

        IActionResult result = await CreateController().LinkWorker(WorkerId, ValidRequest());

        WorkerLinkError response = AssertLinkError(result, StatusCodes.Status503ServiceUnavailable, "RuntimeStopping");
        Assert.Equal("The runtime is stopping.", response.Detail);
        VerifyOnlyLinkCall();
    }

    [Theory]
    [InlineData(typeof(GrpcException))]
    [InlineData(typeof(WorkerRpcException))]
    [InlineData(typeof(HttpRequestException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(SocketException))]
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
            Type type when type == typeof(ChannelClosedException) => new ChannelClosedException(PrivateDiagnostic),
            Type type when type == typeof(OperationCanceledException) => new OperationCanceledException(PrivateDiagnostic),
            Type type when type == typeof(UriFormatException) => new UriFormatException(PrivateDiagnostic),
            _ => throw new ArgumentOutOfRangeException(nameof(exceptionType)),
        };
        _registry.Setup(registry => registry.LinkAsync(WorkerId, new Uri(ValidGrpcEndpoint), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);

        IActionResult result = await CreateController().LinkWorker(WorkerId, ValidRequest()).WaitAsync(TestTimeout);

        WorkerLinkError response = AssertLinkError(result, StatusCodes.Status503ServiceUnavailable, "WorkerUnavailable");
        Assert.Equal("The worker connection or initialization handshake was unavailable.", response.Detail);
        Assert.DoesNotContain(failure.Message, response.Detail, StringComparison.Ordinal);
        VerifyOnlyLinkCall();
    }

    [Fact]
    public async Task LinkWorker_GrpcDeadlineExceeded_ReturnsLinkTimeout()
    {
        _registry.Setup(registry => registry.LinkAsync(WorkerId, new Uri(ValidGrpcEndpoint), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GrpcException(new Status(StatusCode.DeadlineExceeded, PrivateDiagnostic)));

        IActionResult result = await CreateController().LinkWorker(WorkerId, ValidRequest());

        AssertLinkError(result, StatusCodes.Status503ServiceUnavailable, "LinkTimeout");
        VerifyOnlyLinkCall();
    }

    [Fact]
    public async Task LinkWorker_UnknownFailureReason_IsNotReportedAsKnownError()
    {
        _registry.Setup(registry => registry.LinkAsync(WorkerId, new Uri(ValidGrpcEndpoint), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WorkerLinkException((WorkerLinkFailureReason)int.MaxValue, PrivateDiagnostic));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => CreateController().LinkWorker(WorkerId, ValidRequest()));

        VerifyOnlyLinkCall();
    }

    [Fact]
    public async Task LinkWorker_UnexpectedFailure_IsNotReportedAsUnavailable()
    {
        InvalidOperationException failure = new("Programming error.");
        _registry.Setup(registry => registry.LinkAsync(WorkerId, new Uri(ValidGrpcEndpoint), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateController().LinkWorker(WorkerId, ValidRequest()));
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
        _registry.Setup(registry => registry.LinkAsync(WorkerId, new Uri(ValidGrpcEndpoint), null, It.IsAny<CancellationToken>()))
            .Returns(fail
                ? Task.FromException<WorkerLinkResult>(new WorkerLinkException(WorkerLinkFailureReason.Conflict, "Conflict."))
                : Task.FromResult(new WorkerLinkResult(null!, IsNewLink: true)));
        WorkerLinkRequest request = ValidRequest();
        request.WorkerContainerEncryptionKey = "reserved-value-do-not-log";
        WorkerLinkController controller = new(logger.Object, _registry.Object);

        await controller.LinkWorker(WorkerId, request);

        Assert.Single(logger.Invocations.Where(call => string.Equals(call.Method.Name, nameof(ILogger.Log), StringComparison.Ordinal)));
        Assert.Equal(fail ? 1 : 0, eventId.Id);
        Dictionary<string, object> loggedState = Assert.IsType<Dictionary<string, object>>(state);
        Assert.Equal(WorkerId, loggedState["workerId"]);
        Assert.True(Assert.IsType<double>(loggedState["elapsedMilliseconds"]) >= 0);
        Assert.DoesNotContain(request.WorkerContainerEncryptionKey, rendered, StringComparison.Ordinal);
        if (fail)
        {
            Assert.Equal("LinkConflict", loggedState["reason"]);
        }
    }

    [Fact]
    public async Task LinkWorker_NullRequest_ReturnsBadRequestWithoutLinking()
    {
        IReadOnlyList<RequestValidationError> errors = AssertBadRequest(await CreateController().LinkWorker(WorkerId, null));
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
        IReadOnlyList<RequestValidationError> errors = AssertBadRequest(await controller.LinkWorker(WorkerId, ValidRequest()));
        RequestValidationError error = Assert.Single(errors);
        Assert.Equal("InvalidBody", error.Code);
        Assert.Equal("request", error.Target);
        _registry.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LinkWorker_MissingGrpcEndpoint_ReturnsRequiredWithoutLinking()
    {
        IReadOnlyList<RequestValidationError> errors = AssertBadRequest(await CreateController().LinkWorker(WorkerId, new WorkerLinkRequest()));
        RequestValidationError error = Assert.Single(errors);
        Assert.Equal("Required", error.Code);
        Assert.Equal("workerGrpcEndpoint", error.Target);
        _registry.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LinkWorker_MultipleInvalidFields_ReturnsAllErrorsWithoutLinking()
    {
        WorkerLinkRequest request = new()
        {
            WorkerGrpcEndpoint = "relative/grpc",
            WorkerHttpEndpoint = "relative/http",
        };
        IReadOnlyList<RequestValidationError> errors = AssertBadRequest(await CreateController().LinkWorker(WorkerId, request));
        Assert.Equal(
            [("workerGrpcEndpoint", "InvalidEndpoint"), ("workerHttpEndpoint", "InvalidEndpoint")],
            errors.Select(error => (error.Target, error.Code)));
        _registry.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    [InlineData("\u00a0\u2003")]
    public async Task LinkWorker_InvalidRouteWorkerPodName_ReturnsBadRequestWithoutLinking(string? workerPodName)
    {
        IReadOnlyList<RequestValidationError> errors = AssertBadRequest(await CreateController().LinkWorker(workerPodName!, ValidRequest()));
        RequestValidationError error = Assert.Single(errors);
        Assert.Equal("Required", error.Code);
        Assert.Equal("workerPodName", error.Target);
        _registry.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("a")]
    [InlineData("Worker-Pod")]
    [InlineData("w\u00f6rker")]
    [InlineData("worker_pod")]
    [InlineData("-worker-")]
    [InlineData("worker..pod")]
    [InlineData(" worker-pod")]
    [InlineData("worker-pod ")]
    [InlineData("worker pod")]
    [InlineData("worker\tpod")]
    [InlineData("worker\r\npod")]
    [InlineData("worker\u00a0pod")]
    [InlineData("worker\u2003pod")]
    [InlineData("worker\0pod")]
    [InlineData("worker\u001bpod")]
    [InlineData("worker\u007fpod")]
    [InlineData("worker\u009fpod")]
    [InlineData("\0")]
    public async Task LinkWorker_ValidOpaqueWorkerPodName_PreservesIdentity(string workerPodName)
    {
        _registry.Setup(registry => registry.LinkAsync(workerPodName, new Uri(ValidGrpcEndpoint), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkerLinkResult(null!, IsNewLink: true));

        StatusCodeResult result = Assert.IsType<StatusCodeResult>(await CreateController().LinkWorker(workerPodName, ValidRequest()));

        Assert.Equal(StatusCodes.Status201Created, result.StatusCode);
        _registry.Verify(registry => registry.LinkAsync(workerPodName, new Uri(ValidGrpcEndpoint), null, It.IsAny<CancellationToken>()), Times.Once);
        _registry.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(64)]
    [InlineData(253)]
    [InlineData(254)]
    [InlineData(1024)]
    public Task LinkWorker_WorkerPodNameLength_DoesNotRestrictIdentity(int length)
        => LinkWorker_ValidOpaqueWorkerPodName_PreservesIdentity(new string('a', length));

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
        IReadOnlyList<RequestValidationError> errors = AssertBadRequest(await CreateController().LinkWorker(WorkerId, request));
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
        IReadOnlyList<RequestValidationError> errors = AssertBadRequest(await CreateController().LinkWorker(WorkerId, request));
        RequestValidationError error = Assert.Single(errors);
        Assert.Equal("InvalidEndpoint", error.Code);
        Assert.Equal("workerHttpEndpoint", error.Target);
        _registry.VerifyNoOtherCalls();
    }

    private WorkerLinkController CreateController()
        => new(NullLogger<WorkerLinkController>.Instance, _registry.Object);

    private void VerifyOnlyLinkCall(Uri? httpEndpoint = null)
    {
        _registry.Verify(registry => registry.LinkAsync(WorkerId, It.IsAny<Uri>(), httpEndpoint, It.IsAny<CancellationToken>()), Times.Once);
        _registry.VerifyNoOtherCalls();
    }

    private static WorkerLinkError AssertLinkError(IActionResult result, int statusCode, string code)
    {
        ObjectResult failure = Assert.IsType<ObjectResult>(result);
        Assert.Equal(statusCode, failure.StatusCode);
        WorkerLinkErrorResponse envelope = Assert.IsType<WorkerLinkErrorResponse>(failure.Value);
        WorkerLinkError response = envelope.Error;
        Assert.Equal(code, response.Code);
        Assert.False(string.IsNullOrWhiteSpace(response.Detail));
        Assert.DoesNotContain(PrivateDiagnostic, response.Detail, StringComparison.Ordinal);
        return response;
    }

    private static WorkerLinkRequest ValidRequest()
        => new() { WorkerGrpcEndpoint = ValidGrpcEndpoint };

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
