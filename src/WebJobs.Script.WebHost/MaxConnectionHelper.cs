// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    internal static class MaxConnectionHelper
    {
        private const string AzureWebsiteSku = "WEBSITE_SKU";
        private const string DynamicSku = "Dynamic";

        private static bool isSet = false;

        // Temporary helper until https://github.com/Azure/azure-storage-net/issues/580 is fixed.
        public static void SetMaxConnectionsPerServer(ILogger logger)
        {
            if (IsDynamicSku() && !isSet)
            {
                Assembly storageAssembly = typeof(CloudStorageAccount).Assembly;
                Type httpHandlerType = storageAssembly.GetType("Microsoft.WindowsAzure.Storage.Auth.Protocol.StorageAuthenticationHttpHandler");
                PropertyInfo instanceProp = httpHandlerType?.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public);

                if (instanceProp?.GetValue(null) is HttpClientHandler handler)
                {
                    // This is a best-effort attempt. It's possible that the StorageClient
                    // is already in-use and this will throw.
                    try
                    {
                        handler.MaxConnectionsPerServer = 50;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning($"Azure Storage HttpClientHandler was not updated: '{ex}'. Current value: {handler.MaxConnectionsPerServer}");
                    }
                }
                else
                {
                    logger.LogWarning($"Azure Storage HttpClientHandler was not found in assembly: '{storageAssembly.FullName}'.");
                }

                isSet = true;
            }
        }

        private static bool IsDynamicSku()
        {
            string sku = Environment.GetEnvironmentVariable(AzureWebsiteSku);
            return sku != null && sku == DynamicSku;
        }
    }
}