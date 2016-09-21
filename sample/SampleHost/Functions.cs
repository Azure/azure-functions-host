// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs;
using Newtonsoft.Json.Linq;

namespace SampleHost
{
    public static class Functions
    {
        public static void BlobTrigger(
            [BlobTrigger("test")] string blob)
        {
            Console.WriteLine("Processed blob: " + blob);
        }

        public static void BlobPoisonBlobHandler(
            [QueueTrigger("webjobs-blobtrigger-poison")] JObject blobInfo)
        {
            string container = (string)blobInfo["ContainerName"];
            string blobName = (string)blobInfo["BlobName"];

            Console.WriteLine($"Poison blob: {container}/{blobName}");
        }

        public static void QueueTrigger(
            [QueueTrigger("test")] string message)
        {
            Console.WriteLine("Processed message: " + message);
        }
    }
}
