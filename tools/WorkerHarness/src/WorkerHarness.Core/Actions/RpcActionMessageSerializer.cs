// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorkerHarness.Core.Actions
{
    internal static class RpcActionMessageSerializer
    {
        internal static string Serialize(this RpcActionMessage rpcActionMessage)
        {
            JsonSerializerOptions serializerOptions = new()
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            serializerOptions.Converters.Add(new JsonStringEnumConverter());

            return JsonSerializer.Serialize(rpcActionMessage, serializerOptions);
        }
    }
}
