// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Host
{
    /// <summary>
    /// This helper addresses an issue in Azure Storage SDK where if certain assemblies are missing
    /// the Storage SDK actually continues to work overall, but some features will silently fail.
    /// This directly affects our StorageExceptionExtensions which rely on ExtendedErrorInfo being correctly
    /// populated always. However, when certain assemblies are missing, ExtendedErrorInfo will be null.
    /// See related issues:
    /// <see href="https://github.com/Azure/azure-webjobs-sdk/issues/922"/>
    /// <see href="https://github.com/Azure/azure-storage-net/issues/271"/> 
    /// </summary>
    internal static class AzureStorageDeploymentValidator
    {
        public static void Validate()
        {
            try
            {
                VerifyTableServiceAssemblyLoad();
            }
            catch (Exception ex)
            {
                if (ex.IsFatal())
                {
                    throw;
                }
                throw new InvalidOperationException("Microsoft.WindowsAzure.Storage is deployed incorrectly. Are you missing a Table Service assembly (Microsoft.Data.Services.Client, Microsoft.Data.OData or Microsoft.Data.Edm) or a related binding redirect?", ex);
            }
        }

        private static void VerifyTableServiceAssemblyLoad()
        {

#if !NETSTANDARD2_0
            // this forces the relevant assemblies to load so we can catch issues early
#pragma warning disable 618
            using (var ignore = new TableServiceContext(new CloudTableClient(new Uri("http://test.core.windows.net"), null)))
            {
            }
#pragma warning restore 618
#endif
        }
    }
}
