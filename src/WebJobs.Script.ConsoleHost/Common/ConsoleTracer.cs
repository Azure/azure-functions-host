// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;

namespace WebJobs.Script.ConsoleHost.Common
{
    public class ConsoleTracer : ITracer
    {
        public async Task WriteAsync(string value)
        {
            await Console.Out.WriteAsync(value);
        }

        public async Task WriteLineAsync(string value)
        {
            await Console.Out.WriteLineAsync(value);
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }
    }
}
