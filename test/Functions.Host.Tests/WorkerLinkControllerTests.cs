// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Host.Controllers;
using Azure.Functions.Host.Models;
using Azure.Functions.Host.WorkerLink;
using Azure.Functions.Rpc.Client;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Azure.Functions.Host.Tests;

public class WorkerLinkControllerTests
{
    private const string WorkerId = "worker-pod-abc123";
    private const string ValidGrpcEndpoint = "http://100.64.1.12:50053";
    private readonly Mock<IWorkerLinker> _linker = new(MockBehavior.Strict);

    [Theory]
    [InlineData("http://100.64.1.12:50053", null)]
    [InlineData("http://100.64.1.12:50053", "http://100.64.1.12:48801")]
    [InlineData("https://100.64.1.12:50053", "https://100.64.1.12:48801")]
    public async Task LinkWorker_ValidRequest_AwaitsLinkAndReturnsLinked(string grpcEndpoint, string? httpEndpoint)
    {
        using CancellationTokenSource cancellation = new();
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _linker.Setup(linker => linker.LinkAsync(WorkerId, new Uri(grpcEndpoint), cancellation.Token))
            .Returns(completion.Task);
        WorkerLinkRequest request = ValidRequest();
        request.WorkerGrpcEndpoint = grpcEndpoint;
        request.WorkerHttpEndpoint = httpEndpoint;
        request.WorkerContainerEncryptionKey = "reserved-test-value";

        Task<IActionResult> link = CreateController().LinkWorker(request, cancellation.Token);
        Assert.False(link.IsCompleted);
        completion.SetResult();
        var result = Assert.IsType<OkObjectResult>(await link);
        var response = Assert.IsType<WorkerLinkResponse>(result.Value);
        Assert.Equal(WorkerId, response.WorkerPodName);
        Assert.Equal(WorkerLinkStatus.Linked, response.Status);
        Assert.Null(response.Detail);
        _linker.VerifyAll();
    }

    [Theory]
    [InlineData(WorkerLinkFailureReason.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(WorkerLinkFailureReason.WorkerTerminated, StatusCodes.Status409Conflict)]
    [InlineData(WorkerLinkFailureReason.RuntimeStopping, StatusCodes.Status409Conflict)]
    [InlineData(WorkerLinkFailureReason.Unavailable, StatusCodes.Status503ServiceUnavailable)]
    public async Task LinkWorker_ExpectedFailure_ReturnsLinkFailed(WorkerLinkFailureReason reason, int status)
    {
        _linker.Setup(linker => linker.LinkAsync(WorkerId, new Uri(ValidGrpcEndpoint), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WorkerLinkException(reason, "Safe failure detail.", new Exception("Internal diagnostics.")));

        var result = Assert.IsType<ObjectResult>(await CreateController().LinkWorker(ValidRequest()));
        Assert.Equal(status, result.StatusCode);
        var response = Assert.IsType<WorkerLinkResponse>(result.Value);
        Assert.Equal(WorkerId, response.WorkerPodName);
        Assert.Equal(WorkerLinkStatus.LinkFailed, response.Status);
        Assert.Equal("Safe failure detail.", response.Detail);
    }

    [Fact]
    public async Task LinkWorker_CanceledRequest_PropagatesCancellation()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        _linker.Setup(linker => linker.LinkAsync(WorkerId, new Uri(ValidGrpcEndpoint), cancellation.Token))
            .Returns(Task.FromCanceled(cancellation.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateController().LinkWorker(ValidRequest(), cancellation.Token));
    }

    [Fact]
    public async Task LinkWorker_UnexpectedFailure_IsNotReportedAsUnavailable()
    {
        InvalidOperationException failure = new("Programming error.");
        _linker.Setup(linker => linker.LinkAsync(WorkerId, new Uri(ValidGrpcEndpoint), It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateController().LinkWorker(ValidRequest()));
        Assert.Same(failure, actual);
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
        _linker.Setup(linker => linker.LinkAsync(WorkerId, new Uri(ValidGrpcEndpoint), It.IsAny<CancellationToken>()))
            .Returns(fail
                ? Task.FromException(new WorkerLinkException(WorkerLinkFailureReason.Conflict, "Conflict."))
                : Task.CompletedTask);
        WorkerLinkRequest request = ValidRequest();
        request.WorkerContainerEncryptionKey = "reserved-value-do-not-log";
        WorkerLinkController controller = new(logger.Object, _linker.Object);

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
        _linker.VerifyNoOtherCalls();
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
        _linker.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LinkWorker_BothRequiredFieldsMissing_ReturnsBothErrorsWithoutLinking()
    {
        IReadOnlyList<RequestValidationError> errors = AssertBadRequest(await CreateController().LinkWorker(new WorkerLinkRequest()));
        Assert.Equal(
            [("workerPodName", "Required"), ("workerGrpcEndpoint", "Required")],
            errors.Select(error => (error.Target, error.Code)));
        _linker.VerifyNoOtherCalls();
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
        _linker.VerifyNoOtherCalls();
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
        _linker.VerifyNoOtherCalls();
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
        _linker.VerifyNoOtherCalls();
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
        _linker.VerifyNoOtherCalls();
    }

    private WorkerLinkController CreateController()
        => new(NullLogger<WorkerLinkController>.Instance, _linker.Object);

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
