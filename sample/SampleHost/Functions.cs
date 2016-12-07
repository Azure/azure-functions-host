// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs;

namespace SampleHost
{
    public static class Functions
    {
        public static void QueueTrigger([QueueTrigger("test")] string message)
        {
            Console.WriteLine("Processed message: " + message);
        }
    }
}
