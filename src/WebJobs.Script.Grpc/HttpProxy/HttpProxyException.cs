// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace Microsoft.Azure.WebJobs.Script.Grpc.HttpProxy;

internal sealed class HttpProxyException(HttpProxyFailure.Types.Code code)
    : Exception("The HTTP proxy request failed.")
{
    public HttpProxyFailure.Types.Code Code { get; } = code;
}
