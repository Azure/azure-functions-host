// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.TestCommon
{
    public class TestJobHost<TProgram> : JobHost
    {
        public TestJobHost(JobHostConfiguration serviceProvider)
            : base(serviceProvider)
        {
        }

        public void Call(string methodName)
        {
            base.Call(typeof(TProgram).GetMethod(methodName));
        }

        public void Call(string methodName, object arguments)
        {
            base.Call(typeof(TProgram).GetMethod(methodName), arguments);
        }

        public void Call(string methodName, IDictionary<string, object> arguments)
        {
            base.Call(typeof(TProgram).GetMethod(methodName), arguments);
        }

        public Task CallAsync(string methodName, IDictionary<string, object> arguments,
            CancellationToken cancellationToken)
        {
            return base.CallAsync(typeof(TProgram).GetMethod(methodName), arguments, cancellationToken);
        }

        public Task CallAsync(string methodName, object arguments)
        {
            return base.CallAsync(typeof(TProgram).GetMethod(methodName), arguments);
        }

        // Helper for quickly testing indexing errors 
        public void AssertIndexingError(string methodName, string expectedErrorMessage)
        {
            try
            {
                // Indexing is lazy, so must actually try a call first. 
                this.Call(methodName);
            }
            catch (FunctionIndexingException e)
            {
                string functionName = typeof(TProgram).Name + "." + methodName;
                Assert.Equal("Error indexing method '" + functionName + "'", e.Message);
                Assert.True(e.InnerException.Message.Contains(expectedErrorMessage));
                return;
            }
            Assert.True(false, "Invoker should have failed");
        }
    }
}
