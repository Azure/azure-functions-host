// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
using System.Text.Json.Nodes;
using WorkerHarness.Core.Variables;

namespace WorkerHarness.Core.StreamingMessageService
{
    /// <summary>
    /// Abtraction to create a StreamingMessage
    /// </summary>
    public interface IStreamingMessageProvider
    {
        /// <summary>
        /// Create a StreamingMessage object from a payload
        /// </summary>
        /// <param name="messageType" cref="string">the type of StreamingMessage</param>
        /// <param name="payload" cref="string">the payload to create the StreamingMessage</param>
        /// <returns cref="StreamingMessage"></returns>
        StreamingMessage Create(string messageType, JsonNode? payload);

        /// <summary>
        /// Create a StreamingMessage object from a payload that may contain variables
        /// </summary>
        /// <param name="message" cref="StreamingMessage">a StreamingMessage of type "messageType" if all variables are solve; a StreamingMessage of type "None" otherwise</param>
        /// <param name="messageType" cref="string">type of StreamingMessage</param>
        /// <param name="payload" cref="JsonNode">the payload to create the StreamingMessage</param>
        /// <param name="variableObservable" cref="IVariableObservable"></param>
        /// <returns cref="bool">true if a "messageType" StreamingMessage is created; false otherwise</returns>
        bool TryCreate(out StreamingMessage message, string messageType, JsonNode? payload, IVariableObservable variableObservable);
    }
}
