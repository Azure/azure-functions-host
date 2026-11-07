// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Grpc;

/// <summary>
/// Indicates that no suitable worker channel became available before the readiness timeout.
/// </summary>
/// <param name="message">A description of the worker-channel readiness timeout.</param>
public sealed class WorkerChannelUnavailableException(string message) : TimeoutException(message);
