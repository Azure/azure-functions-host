// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management.LinuxSpecialization
{
    public interface IManagedIdentityTokenProvider
    {
        Task<string> GetManagedIdentityToken(string resourceUrl);
    }
}