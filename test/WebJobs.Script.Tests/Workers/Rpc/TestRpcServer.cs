// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Abstractions.Rpc;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc
{
    public class TestRpcServer : IRpcServer
    {
        private Uri _testUri = null;

        public Uri Uri => _testUri != null ? _testUri : new Uri($"http://127.0.0.1:8797");

        public Task KillAsync()
        {
            _testUri = null;
            return Task.CompletedTask;
        }

        public Task ShutdownAsync()
        {
            _testUri = null;
            return Task.CompletedTask;
        }

        public Task StartAsync()
        {
            _testUri = new Uri($"http://testServer:8797");
            return Task.CompletedTask;
        }
    }
}
