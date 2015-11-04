// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Data.Entity;
using Microsoft.Azure.WebJobs.Host;

namespace Host
{
    public static partial class Functions
    {
        public static void QueueTrigger(Post post, TraceWriter trace)
        {
            DbContext context = new DbContext("myconnection");

            trace.Info(string.Format("C# QueueTrigger function processed post '{0}'", post.Text));
        }
    }
}
