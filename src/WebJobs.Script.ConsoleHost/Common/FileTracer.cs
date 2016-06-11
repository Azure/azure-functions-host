// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using System.Threading.Tasks;

namespace WebJobs.Script.ConsoleHost.Common
{
    public class FileTracer : ITracer
    {
        private readonly StreamWriter _file;
        public FileTracer(string filePath)
        {
            _file = new StreamWriter(filePath, append: true)
            {
                AutoFlush = true
            };
        }

        public async Task WriteAsync(string value)
        {
            await _file.WriteAsync(value);
        }

        public async Task WriteLineAsync(string value)
        {
            await _file.WriteLineAsync(value);
        }

        public void Dispose()
        {
            _file.Dispose();
        }
    }
}
