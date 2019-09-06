// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Models;

namespace Microsoft.Azure.WebJobs.Script.ExtensionBundle
{
    public interface IExtensionBundleManager
    {
        Task<string> GetExtensionBundlePath();

        Task<string> GetExtensionBundlePath(HttpClient httpClient = null);

        bool IsExtensionBundleConfigured();

        Task<ExtensionBundleDetails> GetExtensionBundleDetails();
    }
}