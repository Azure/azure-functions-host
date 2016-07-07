// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using System.Threading.Tasks;

namespace WebJobs.Script.Cli.Tests
{
    public static class Extensions
    {
        public static Stream ToStream(this string value)
        {
            var stream = new MemoryStream();
            var writer = new StreamWriter(stream);
            writer.Write(value);
            writer.Flush();
            stream.Position = 0;
            return stream;
        }

        public static Task<T> AsTask<T>(this T obj)
        {
            return Task.FromResult(obj);
        }
    }
}
