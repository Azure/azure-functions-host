// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Primitives;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public interface IStandbyManager
    {
        IChangeToken GetChangeToken();

        Task InitializeAsync();

        Task SpecializeHostAsync();
    }
}