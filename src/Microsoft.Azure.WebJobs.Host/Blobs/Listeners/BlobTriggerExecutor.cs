// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Listeners
{
    internal class BlobTriggerExecutor : ITriggerExecutor<ICloudBlob>
    {
        private readonly IBlobPathSource _input;
        private readonly IEnumerable<IBindableBlobPath> _outputs;
        private readonly ITriggeredFunctionInstanceFactory<ICloudBlob> _instanceFactory;
        private readonly IFunctionExecutor _innerExecutor;
        private readonly IBlobTimestampReader _timestampReader;

        public BlobTriggerExecutor(IBlobPathSource input, IEnumerable<IBindableBlobPath> outputs,
            ITriggeredFunctionInstanceFactory<ICloudBlob> instanceFactory, IFunctionExecutor innerExecutor)
        {
            _input = input;
            _outputs = outputs;
            _instanceFactory = instanceFactory;
            _innerExecutor = innerExecutor;
            _timestampReader = BlobTimestampReader.Instance;
        }

        public async Task<bool> ExecuteAsync(ICloudBlob value, CancellationToken cancellationToken)
        {
            if (!await ShouldExecuteTriggerAsync(value, cancellationToken))
            {
                return true;
            }

            Guid? parentId = await BlobCausalityManager.GetWriterAsync(value, cancellationToken);
            IFunctionInstance instance = _instanceFactory.Create(value, parentId);
            IDelayedException exception = await _innerExecutor.TryExecuteAsync(instance, cancellationToken);
            return exception == null;
        }

        private Task<bool> ShouldExecuteTriggerAsync(ICloudBlob possibleTrigger, CancellationToken cancellationToken)
        {
            return ShouldExecuteTriggerAsync(possibleTrigger, _input, _outputs, _timestampReader, cancellationToken);
        }

        internal static async Task<bool> ShouldExecuteTriggerAsync(ICloudBlob possibleTrigger, IBlobPathSource input,
            IEnumerable<IBindableBlobPath> outputs, IBlobTimestampReader timestampReader,
            CancellationToken cancellationToken)
        {
            // Avoid unnecessary network calls for non-matches. First, check to see if the blob matches this trigger.
            IReadOnlyDictionary<string, object> bindingData = input.CreateBindingData(possibleTrigger.ToBlobPath());

            if (bindingData == null)
            {
                // Blob is not a match for this trigger.
                return false;
            }

            // Next, check to see if the blob currently exists.
            DateTime? possibleInputTimestamp = await timestampReader.GetLastModifiedTimestampAsync(possibleTrigger,
                cancellationToken);

            if (!possibleInputTimestamp.HasValue)
            {
                // If the blob doesn't exist and have a timestamp, don't trigger on it.
                return false;
            }

            DateTime inputTimestamp = possibleInputTimestamp.Value;
            CloudBlobClient client = possibleTrigger.ServiceClient;

            // Finally, if there are outputs, check to see if they are all newer than the input.
            if (outputs != null && outputs.Any())
            {
                foreach (IBindableBlobPath output in outputs)
                {
                    if (!await IsOutputNewerThanAsync(inputTimestamp, client, bindingData, output, timestampReader,
                        cancellationToken))
                    {
                        return true;
                    }
                }

                return false;
            }

            return true;
        }

        private static async Task<bool> IsOutputNewerThanAsync(DateTime inputTimestamp, CloudBlobClient client,
            IReadOnlyDictionary<string, object> bindingData, IBindableBlobPath output,
            IBlobTimestampReader timestampReader, CancellationToken cancellationToken)
        {
            BlobPath outputPath = output.Bind(bindingData);

            // Assumes inputs and outputs are in the same storage account.
            CloudBlobContainer outputContainer = client.GetContainerReference(outputPath.ContainerName);
            CloudBlockBlob outputBlob = outputContainer.GetBlockBlobReference(outputPath.BlobName);
            DateTime? possibleOutputTimestamp = await timestampReader.GetLastModifiedTimestampAsync(outputBlob,
                cancellationToken);

            if (!possibleOutputTimestamp.HasValue)
            {
                // If the output blob has no timestamp, it's not newer than the input blob.
                return false;
            }

            DateTime outputTimestamp = possibleOutputTimestamp.Value;

            return outputTimestamp > inputTimestamp;
        }
    }
}
