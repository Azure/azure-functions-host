// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Workers;

/// <summary>
/// Describes the runtime reported by a running language worker.
/// </summary>
/// <param name="RuntimeName">The runtime name reported by the worker, for example <c>node</c>.</param>
/// <param name="RuntimeVersion">The runtime version reported by the worker, for example <c>22.11.0</c>.</param>
/// <param name="WorkerVersion">The language worker version reported by the worker.</param>
public sealed record WorkerRuntimeInfo(string RuntimeName, string RuntimeVersion, string WorkerVersion);
