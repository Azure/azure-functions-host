// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace WebJobs.Script.Cli.Arm.Models
{
    internal class ArmArrayWrapper<T>
    {
        [JsonProperty(PropertyName = "value")]
        public IEnumerable<ArmWrapper<T>> Value { get; set; }
    }
}