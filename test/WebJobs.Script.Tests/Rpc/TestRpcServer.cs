// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Abstractions;

namespace Microsoft.Azure.WebJobs.Script.Tests.Rpc
{
    public class TestRpcServer : IRpcServer
    {
        public Uri Uri => throw new NotImplementedException();

        public Task ShutdownAsync()
        {
            throw new NotImplementedException();
        }

        public Task StartAsync()
        {
            throw new NotImplementedException();
        }
    }
}
