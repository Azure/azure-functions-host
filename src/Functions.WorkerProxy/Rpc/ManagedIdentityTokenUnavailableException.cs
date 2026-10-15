// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Azure.Functions.WorkerProxy.Rpc;

internal sealed class ManagedIdentityTokenUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
