// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Management.Models;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management
{
    public interface IWebFunctionsManager
    {
        Task<IEnumerable<FunctionMetadataResponse>> GetFunctionsMetadata(HttpRequest request);

        Task<(bool, FunctionMetadataResponse)> TryGetFunction(string name, HttpRequest request);

        Task<(bool, bool, FunctionMetadataResponse)> CreateOrUpdate(string name, FunctionMetadataResponse functionMetadata, HttpRequest request);

        (bool, string) TryDeleteFunction(FunctionMetadataResponse function);

        Task<(bool success, string error)> TrySyncTriggers();
    }
}
