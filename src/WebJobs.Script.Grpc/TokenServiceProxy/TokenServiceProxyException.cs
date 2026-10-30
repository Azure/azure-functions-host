// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace Microsoft.Azure.WebJobs.Script.Grpc.TokenServiceProxy;

/// <summary>
/// Reports a Token Service proxy failure using a protocol error code without request or credential data.
/// </summary>
/// <param name="code">The failure code to return to the peer.</param>
public sealed class TokenServiceProxyException(HttpProxyFailure.Types.Code code)
    : Exception("The HTTP proxy request failed.")
{
    /// <summary>
    /// Gets the protocol failure code.
    /// </summary>
    public HttpProxyFailure.Types.Code Code { get; } = code;
}
