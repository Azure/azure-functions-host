// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs;

namespace Host
{
    public static partial class Functions
    {
        // Demonstrates that you can also use the full WebJobs SDK attribute
        // programming model explicitly on your functions (instead of declaring
        // metadata in the manifest.json file)
        [Singleton]
        public static void QueueTrigger_Explicit(
            [QueueTrigger("samples-input")] string input,
            [Blob("samples-output/{id}")] out string output)
        {
            output = input;
        }
    }
}
