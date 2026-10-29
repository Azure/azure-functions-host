// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc;

internal sealed class RpcException(string result, string message, string stack)
    : Exception($"Result: {result}\nType: \nException: {message}\nStack: {stack}");
