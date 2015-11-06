// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;

namespace Host
{
    public static partial class Functions
    {
        public static void QueueTrigger(Post post, TraceWriter trace)
        {
            trace.Info(string.Format("C# QueueTrigger function processed post '{0}'", post.Text));
        }

        // Demonstrates that you can also use the WebJobs SDK explicitly on
        // your functions if you want to
        [Singleton]
        public static void QueueTrigger_Explicit(
            [QueueTrigger("samples-input")] string input,
            [Blob("samples-output/{id}")] out string output)
        {
            output = input;
        }
    }
}
