// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Eventing;

namespace Microsoft.Azure.WebJobs.Script
{
    public class DebugNotification : ScriptEvent
    {
        public DebugNotification(string source, DateTime notificationTime)
            : base(nameof(DebugNotification), source)
        {
            NotificationTime = notificationTime;
        }

        public DateTime NotificationTime { get; }
    }
}
