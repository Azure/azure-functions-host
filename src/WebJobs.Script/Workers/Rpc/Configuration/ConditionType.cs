// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Runtime.Serialization;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    public enum ConditionType
    {
        [EnumMember(Value = "environment")]
        Environment,
        [EnumMember(Value = "hostProperty")]
        HostProperty
    }
}