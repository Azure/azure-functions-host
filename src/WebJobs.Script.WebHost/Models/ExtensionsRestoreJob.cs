// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    public class ExtensionsRestoreJob
    {
        public ExtensionsRestoreJob()
        {
            Id = Guid.NewGuid().ToString();
            Status = ExtensionsRestoreStatus.Started;
            StartTime = DateTimeOffset.Now;
        }

        public string Id { get; set; }

        public ExtensionsRestoreStatus Status { get; set; }

        public DateTimeOffset StartTime { get; set; }

        public DateTimeOffset? EndTime { get; set; }

        public string Error { get; set; }

        public IDictionary<string, string> Properties { get; set; }
    }
}
