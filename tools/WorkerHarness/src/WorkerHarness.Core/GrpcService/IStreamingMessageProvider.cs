// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
using System.Text.Json.Nodes;

namespace WorkerHarness.Core.GrpcService
{
    /// <summary>
    /// Abtract the responsibility to create a Grpc Message
    /// </summary>
    public interface IStreamingMessageProvider
    {
        /// <summary>
        /// Create a StreamingMessage object
        /// </summary>
        /// <param name="contentCase" cref="string">the type of StreamingMessage</param>
        /// <param name="content" cref="string">the content to create the StreamingMessage</param>
        /// <returns></returns>
        StreamingMessage Create(string contentCase, JsonNode? content);
    }
}
