// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Loggers
{
    // Wrap facilities for logging a function's output. 
    // This means capturing console out, redirecting to a TraceWriter that is available at a blob.
    // Handle incremental updates to get real-time updates for long running functions. 
    internal sealed class BlobFunctionOutputDefinition : IFunctionOutputDefinition
    {
        private readonly IStorageBlobClient _client;
        private readonly LocalBlobDescriptor _outputBlob;
        private readonly LocalBlobDescriptor _parameterLogBlob;

        public BlobFunctionOutputDefinition(IStorageBlobClient client, LocalBlobDescriptor outputBlob,
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

        public async Task<IFunctionOutput> CreateOutputAsync(CancellationToken cancellationToken)
        {
            IStorageBlockBlob blob = GetBlockBlobReference(_outputBlob);
            string existingContents = await ReadBlobAsync(blob, cancellationToken);
            return await UpdateOutputLogCommand.CreateAsync(blob, existingContents, cancellationToken);
        }

        public IRecurrentCommand CreateParameterLogUpdateCommand(IReadOnlyDictionary<string, IWatcher> watches, TraceWriter trace)
        {
            return new UpdateParameterLogCommand(watches, GetBlockBlobReference(_parameterLogBlob), trace);
        }

        private IStorageBlockBlob GetBlockBlobReference(LocalBlobDescriptor descriptor)
        {
            IStorageBlobContainer container = _client.GetContainerReference(descriptor.ContainerName);
            return container.GetBlockBlobReference(descriptor.BlobName);
        }

        // Return Null if doesn't exist
        [DebuggerNonUserCode]
        private static async Task<string> ReadBlobAsync(IStorageBlob blob, CancellationToken cancellationToken)
        {
            // Beware! Blob.DownloadText does not strip the BOM!
            try
            {
                using (var stream = await blob.OpenReadAsync(cancellationToken))
                using (StreamReader sr = new StreamReader(stream, detectEncodingFromByteOrderMarks: true))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string data = await sr.ReadToEndAsync();
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
