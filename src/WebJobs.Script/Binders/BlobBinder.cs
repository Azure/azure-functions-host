// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Binders
{
    internal class BlobBinder
    {
        private readonly IBinder _binder;

        private BlobBinder(IBinder binder)
        {
            _binder = binder;
        }

        public static Dictionary<string, object> Create(IBinder binder)
        {
            BlobBinder blobBinder = new BlobBinder(binder);

            return new Dictionary<string, object>()
            {
                { "read", (Func<object, Task<object>>)blobBinder.Read },
                { "write", (Func<object, Task<object>>)blobBinder.Write }
            };
        }

        public async Task<object> Write(object options)
        {
            // TODO: need to handle other data types (e.g. byte[], etc.)
            var optionsDictionary = (IDictionary<string, object>)options;
            string path = (string)optionsDictionary["path"];
            string data = (string)optionsDictionary["data"];

            Stream stream = _binder.Bind<Stream>(new BlobAttribute(path, FileAccess.Write));
            using (StreamWriter sw = new StreamWriter(stream))
            {
                await sw.WriteLineAsync(data);
            }
            return Task.FromResult<object>(null);
        }

        public async Task<object> Read(object options)
        {
            // TODO: need to handle other data types (e.g. byte[], etc.)
            var optionsDictionary = (IDictionary<string, object>)options;
            string path = (string)optionsDictionary["path"];

            Stream stream = _binder.Bind<Stream>(new BlobAttribute(path, FileAccess.Read));
            using (StreamReader sr = new StreamReader(stream))
            {
                return await sr.ReadToEndAsync();
            }
        }
    }
}
