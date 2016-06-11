// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;

namespace WebJobs.Script.ConsoleHost.Common
{
    public interface ITracer : IDisposable
    {
        Task WriteAsync(string value);
        Task WriteLineAsync(string value);
    }
}
