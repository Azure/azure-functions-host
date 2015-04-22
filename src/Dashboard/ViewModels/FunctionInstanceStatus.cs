// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Dashboard.ViewModels
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum FunctionInstanceStatus
    {
        None, // shouldn't be used. Can indicate a serialization error.
        Queued, // Queued from Dashboard but the host has not yet started running it.
        Running, // Started running (first status for non-Dashboard invoked functions).
        CompletedSuccess, // ran to completion, either via success or a user error (threw exception)
        CompletedFailed, // ran to completion, but function through an exception before finishing
        NeverFinished // Had not finished when host stopped running
    }
}
