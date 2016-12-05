// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class TestInvoker : IFunctionInvoker
    {
        private readonly Action<object[]> _invokeCallback;
        private int _invokeCount = 0;

        public TestInvoker(Action<object[]> invokeCallback = null)
        {
            _invokeCallback = invokeCallback
                ?? new Action<object[]>(_ => { });
        }

        public int InvokeCount
        {
            get
            {
                return _invokeCount;
            }
        }

        public Task Invoke(object[] parameters)
        {
            Interlocked.Increment(ref _invokeCount);
            _invokeCallback(parameters);
            return Task.FromResult(0);
        }

        public void OnError(Exception ex)
        {
        }
    }
}
