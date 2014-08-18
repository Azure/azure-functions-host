// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Host.EndToEndTests
{
    /// <summary>
    /// Converts blobs to/from <see cref="Microsoft.Azure.WebJobs.Host.EndToEndTests.CustomObject"/>
    /// </summary>
    public class BlobToCustomObjectBinder : ICloudBlobStreamBinder<CustomObject>
    {
        public async Task<CustomObject> ReadFromStreamAsync(Stream input, CancellationToken cancellationToken)
        {
            using (StreamReader reader = new StreamReader(input))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string jsonString = await reader.ReadToEndAsync();
                return JsonConvert.DeserializeObject<CustomObject>(jsonString);
            };
        }

        public async Task WriteToStreamAsync(CustomObject value, Stream output, CancellationToken cancellationToken)
        {
            const int defaultBufferSize = 1024;

            using (TextWriter writer = new StreamWriter(output, Encoding.UTF8, defaultBufferSize,
                leaveOpen: true))
            {
                string jsonString = JsonConvert.SerializeObject(value);
                await writer.WriteAsync(jsonString);
                await writer.FlushAsync();
            };
        }
    }
}
