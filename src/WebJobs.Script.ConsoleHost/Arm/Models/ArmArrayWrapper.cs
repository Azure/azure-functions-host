// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace WebJobs.Script.ConsoleHost.Arm.Models
{
    public class ArmArrayWrapper<T>
    {
        public ArmWrapper<T>[] value { get; set; }
    }
}