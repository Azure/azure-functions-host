// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using Azure.Functions.Host.Models;
using Azure.Functions.Rpc.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies;

namespace Azure.Functions.Host.Controllers;

/// <summary>
/// Accepts irreversible BYOC Runtime drain requests in compute separation mode.
/// </summary>
public sealed class ByocRuntimeDrainController : Controller
{
    private readonly IByocRuntimeDrainManager _manager;

    /// <summary>
    /// Initializes a new instance of the <see cref="ByocRuntimeDrainController"/> class.
    /// </summary>
    /// <param name="manager">The Runtime drain coordinator.</param>
    public ByocRuntimeDrainController(IByocRuntimeDrainManager manager)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
    }

    /// <summary>
    /// Starts, replays, or escalates the BYOC Runtime drain.
    /// </summary>
    /// <param name="request">The exact drain model request.</param>
    /// <param name="cancellationToken">Cancels before irreversible admission closes.</param>
    /// <returns>An immutable acceptance receipt, validation errors, or a downgrade conflict.</returns>
    [HttpPost]
    [Route("admin/functions/byocdrain")]
    [Authorize(Policy = PolicyNames.AdminAuthLevel)]
    public IActionResult DrainRuntime(
        [FromBody] ByocRuntimeDrainRequest? request,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RequestValidationError> errors = ValidateRequest(
            request,
            out ByocDrainModel drainModel);
        if (errors.Count > 0)
        {
            return BadRequest(new RequestValidationResponse(errors));
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            ByocRuntimeDrainReceipt receipt = _manager.Accept(drainModel);
            return StatusCode(
                StatusCodes.Status202Accepted,
                new ByocRuntimeDrainResponse(
                    receipt.DrainModel.ToString(),
                    receipt.AcceptedAtUtc));
        }
        catch (ByocRuntimeDrainConflictException exception)
        {
            return StatusCode(
                StatusCodes.Status409Conflict,
                new ByocRuntimeDrainErrorResponse(
                    new(
                        ErrorCodes.DrainConflict,
                        exception.Message)));
        }
    }

    private IReadOnlyList<RequestValidationError> ValidateRequest(
        ByocRuntimeDrainRequest? request,
        out ByocDrainModel drainModel)
    {
        drainModel = default;
        if (!ModelState.IsValid ||
            request is null)
        {
            return [new(ErrorCodes.InvalidBody, "request")];
        }

        if (string.IsNullOrWhiteSpace(request.DrainModel))
        {
            return [new(ErrorCodes.Required, "drainModel")];
        }

        if (string.Equals(
            request.DrainModel,
            nameof(ByocDrainModel.Phased),
            StringComparison.Ordinal))
        {
            drainModel = ByocDrainModel.Phased;
            return [];
        }

        if (string.Equals(
            request.DrainModel,
            nameof(ByocDrainModel.Immediate),
            StringComparison.Ordinal))
        {
            drainModel = ByocDrainModel.Immediate;
            return [];
        }

        return [new(ErrorCodes.InvalidValue, "drainModel")];
    }

    private static class ErrorCodes
    {
        public const string DrainConflict = nameof(DrainConflict);
        public const string InvalidBody = nameof(InvalidBody);
        public const string InvalidValue = nameof(InvalidValue);
        public const string Required = nameof(Required);
    }
}
