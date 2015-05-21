// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Dashboard.ViewModels
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum WebJobType
    {
        Triggered,
        Continuous
    }
}
