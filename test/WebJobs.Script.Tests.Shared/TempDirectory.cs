// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;

namespace WebJobs.Script.Tests
{
    public sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
            : this(System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName()))
        {
        }

        public TempDirectory(string path)
        {
            Path = path;
            Directory.CreateDirectory(path);
        }

        ~TempDirectory()
        {
            Dispose(false);
        }

        public string Path { get; }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            Dispose(true);
        }

        private void Dispose(bool disposing)
        {
            DeleteDirectory();
        }

        private void DeleteDirectory()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch
            {
                // best effort
            }
        }
    }
}
