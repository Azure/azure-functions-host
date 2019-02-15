// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Net.Http;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.BindingExtensionBundle
{
    public interface IExtensionBundleManager
    {
        Task<string> GetExtensionBundle(HttpClient httpClient = null);

        bool IsExtensionBundleConfigured();
    }
}