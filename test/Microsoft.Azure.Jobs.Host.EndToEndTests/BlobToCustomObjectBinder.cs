// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Microsoft.Azure.Jobs.Host.EndToEndTests
{
    /// <summary>
    /// Converts blobs to/from <see cref="Microsoft.Azure.Jobs.Host.EndToEndTests.CustomObject"/>
    /// </summary>
    public class BlobToCustomObjectBinder : ICloudBlobStreamBinder<CustomObject>
    {
        public CustomObject ReadFromStream(Stream input)
        {
            using (StreamReader reader = new StreamReader(input))
            {
                string jsonString = reader.ReadToEnd();
                return JsonConvert.DeserializeObject<CustomObject>(jsonString);
            };
        }

        public void WriteToStream(CustomObject value, Stream output)
        {
            const int defaultBufferSize = 1024;

            using (TextWriter writer = new StreamWriter(output, Encoding.UTF8, defaultBufferSize,
                leaveOpen: true))
            {
                string jsonString = JsonConvert.SerializeObject(value);
                writer.Write(jsonString);
                writer.Flush();
            };
        }
    }
}
