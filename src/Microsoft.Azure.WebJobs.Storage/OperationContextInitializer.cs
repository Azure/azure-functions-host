// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.WindowsAzure.Storage;

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage
#else
namespace Microsoft.Azure.WebJobs.Host.Storage
#endif
{
    internal static class OperationContextInitializer
    {
        static OperationContextInitializer()
        {
            var userAgent = "-AzureWebJobs";
            OperationContext.GlobalSendingRequest += (sender, e) =>
            {
                e.Request.UserAgent += userAgent;
            };
        }

        public static void Initialize()
        {
            // This method is a noop, it exists to expose a convenient
            // way to trigger the static initialization 
        }
    }
}