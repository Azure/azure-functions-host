// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using Azure.Functions.Host.Models;
using Azure.Functions.Host.WorkerLink;
using Azure.Functions.Rpc.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies;

namespace Azure.Functions.Host.Controllers;

/// <summary>
/// Accepts irreversible Runtime drain requests in compute separation mode.
/// </summary>
public sealed class RuntimeDrainController : Controller
{
    private readonly IRuntimeDrainManager _manager;

    /// <summary>
    /// Initializes a new instance of the <see cref="RuntimeDrainController"/> class.
    /// </summary>
    /// <param name="manager">The Runtime drain coordinator.</param>
    public RuntimeDrainController(IRuntimeDrainManager manager)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
    }

    /// <summary>
    /// Starts, replays, or escalates the Runtime drain.
    /// </summary>
    /// <param name="request">The exact drain mode request.</param>
    /// <param name="cancellationToken">Cancels before irreversible admission closes.</param>
    /// <returns>An immutable acceptance receipt, validation errors, or a downgrade conflict.</returns>
    [HttpPost]
    [Route("admin/runtime/drain")]
    [Authorize(Policy = PolicyNames.AdminAuthLevel)]
    public IActionResult DrainRuntime(
        [FromBody] RuntimeDrainRequest? request,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RequestValidationError> errors = ValidateRequest(
            request,
            out RuntimeDrainMode drainMode);
        if (errors.Count > 0)
        {
            return BadRequest(new RequestValidationResponse(errors));
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            RuntimeDrainReceipt receipt = _manager.Accept(drainMode);
            return StatusCode(
                StatusCodes.Status202Accepted,
                new RuntimeDrainResponse(
                    receipt.DrainMode.ToString(),
                    receipt.AcceptedAtUtc));
        }
        catch (RuntimeDrainConflictException exception)
        {
            return StatusCode(
                StatusCodes.Status409Conflict,
                new WorkerLinkErrorResponse(
                    new WorkerLinkError(
                        ErrorCodes.DrainConflict,
                        exception.Message)));
        }
    }

    private IReadOnlyList<RequestValidationError> ValidateRequest(
        RuntimeDrainRequest? request,
        out RuntimeDrainMode drainMode)
    {
        drainMode = default;
        if (!ModelState.IsValid ||
            request is null)
        {
            return [new(RequestValidationErrorCodes.InvalidBody, "request")];
        }

        if (string.IsNullOrWhiteSpace(request.DrainMode))
        {
            return [new(RequestValidationErrorCodes.Required, "drainMode")];
        }

        if (string.Equals(
            request.DrainMode,
            nameof(RuntimeDrainMode.Phased),
            StringComparison.Ordinal))
        {
            drainMode = RuntimeDrainMode.Phased;
            return [];
        }

        if (string.Equals(
            request.DrainMode,
            nameof(RuntimeDrainMode.Immediate),
            StringComparison.Ordinal))
        {
            drainMode = RuntimeDrainMode.Immediate;
            return [];
        }

        return [new(RequestValidationErrorCodes.InvalidValue, "drainMode")];
    }

    private static class ErrorCodes
    {
        public const string DrainConflict = nameof(DrainConflict);
    }
}
