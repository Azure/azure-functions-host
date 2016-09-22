// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace WebJobs.Script.Cli.Interfaces
{
    internal interface IFunctionsLocalServer
    {
        Task<HttpClient> ConnectAsync(TimeSpan timeout);
    }
}