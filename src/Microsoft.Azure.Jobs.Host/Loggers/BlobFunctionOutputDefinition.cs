// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Executors;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.Azure.Jobs.Host.Timers;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Loggers
{
    // Wrap facilities for logging a function's output. 
    // This means capturing console out, redirecting to a textwriter that is available at a blob.
    // Handle incremental updates to get real-time updates for long running functions. 
    internal sealed class BlobFunctionOutputDefinition : IFunctionOutputDefinition
    {
        private readonly CloudBlobClient _client;
        private readonly LocalBlobDescriptor _outputBlob;
        private readonly LocalBlobDescriptor _parameterLogBlob;

        public BlobFunctionOutputDefinition(CloudBlobClient client, LocalBlobDescriptor outputBlob,
            LocalBlobDescriptor parameterLogBlob)
        {
            _client = client;
            _outputBlob = outputBlob;
            _parameterLogBlob = parameterLogBlob;
        }

        public LocalBlobDescriptor OutputBlob
        {
            get { return _outputBlob; }
        }

        public LocalBlobDescriptor ParameterLogBlob
        {
            get { return _parameterLogBlob; }
        }

        public IFunctionOutput CreateOutput()
        {
            CloudBlockBlob blob = GetBlockBlobReference(_outputBlob);
            string existingContents = ReadBlob(blob);
            return new UpdateOutputLogCommand(blob, existingContents);
        }

        public ICanFailCommand CreateParameterLogUpdateCommand(IReadOnlyDictionary<string, IWatcher> watches,
            TextWriter consoleOutput)
        {
            return new UpdateParameterLogCommand(watches, GetBlockBlobReference(_parameterLogBlob), consoleOutput);
        }

        private CloudBlockBlob GetBlockBlobReference(LocalBlobDescriptor descriptor)
        {
            CloudBlobContainer container = _client.GetContainerReference(descriptor.ContainerName);
            return container.GetBlockBlobReference(descriptor.BlobName);
        }

        // Return Null if doesn't exist
        [DebuggerNonUserCode]
        private static string ReadBlob(ICloudBlob blob)
        {
            // Beware! Blob.DownloadText does not strip the BOM! 
            try
            {
                using (var stream = blob.OpenRead())
                using (StreamReader sr = new StreamReader(stream, detectEncodingFromByteOrderMarks: true))
                {
                    string data = sr.ReadToEnd();
                    return data;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
