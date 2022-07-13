// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorkerHarness.Core.StreamingMessageService
{
    internal static class StreamingMessageSerializer
    {
        /// <summary>
        /// Pretty serialize a StreamingMessage object
        /// </summary>
        /// <param name="message" cref="StreamingMessage"></param>
        /// <returns></returns>
        internal static string Serialize(this StreamingMessage message)
        {
            JsonSerializerOptions serializerOptions = new()
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            serializerOptions.Converters.Add(new JsonStringEnumConverter());

            return JsonSerializer.Serialize(message, serializerOptions);
        }
    }
}
