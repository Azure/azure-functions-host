// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json.Serialization;

namespace Microsoft.Azure.WebJobs.Script.Models
{
    [JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, AllowTrailingCommas = true)]
    [JsonSerializable(typeof(ExtensionReferences))]
    public partial class ExtensionReferencesJsonContext : JsonSerializerContext;
}
