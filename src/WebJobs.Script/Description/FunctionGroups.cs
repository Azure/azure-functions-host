// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public static class FunctionGroups
    {
        public const string Http = "http";
        public const string Durable = "durable";
        public const string Blob = "blob";

        public static string ForFunction(string function)
        {
            return $"function:{function}";
        }

        public static bool IsEnabled(string targetGroup, string triggerGroup)
        {
            if (string.Equals(targetGroup, triggerGroup, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(targetGroup, Http, StringComparison.OrdinalIgnoreCase)
                && string.Equals(triggerGroup, Blob, StringComparison.OrdinalIgnoreCase))
            {
                // The blob group needs to be special cased as it is a two step process:
                // 1. WebHook which enqueues a message to Azure Storage Queue.
                // 2. Azure Storage Queue trigger which processes the message.
                // So we need to run in both http and blob groups.
                return true;
            }

            return false;
        }
    }
}