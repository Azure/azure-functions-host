// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.Azure.WebJobs.Script.AppCapabilities
{
    [JsonSerializable(typeof(IDictionary<string, string>))]
    internal partial class DictionaryJsonContext : JsonSerializerContext;
}
