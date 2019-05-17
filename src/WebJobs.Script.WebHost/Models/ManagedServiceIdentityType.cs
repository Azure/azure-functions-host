// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Runtime.Serialization;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    [Flags]
    public enum ManagedServiceIdentityType
    {
        [EnumMember]
        None = 0,
        [EnumMember]
        SystemAssigned = 1,
        [EnumMember]
        UserAssigned = 2
    }
}
