// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.Jobs.Host.Executors;
using Microsoft.Azure.Jobs.Host.Listeners;
using Microsoft.Azure.Jobs.Host.Storage;
using Microsoft.Azure.Jobs.Host.Triggers;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs.Listeners
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

        public bool Execute(ICloudBlob value)
        {
            if (!ShouldExecuteTrigger(value))
            {
                return true;
            }

            Guid? parentId = BlobCausalityManager.GetWriter(value);
            IFunctionInstance instance = _instanceFactory.Create(value, parentId);
            IDelayedException exception = _innerExecutor.TryExecute(instance);
            return exception == null;
        }

        private bool ShouldExecuteTrigger(ICloudBlob possibleTrigger)
        {
            return ShouldExecuteTrigger(possibleTrigger, _input, _outputs, _timestampReader);
        }

        internal static bool ShouldExecuteTrigger(ICloudBlob possibleTrigger, IBlobPathSource input,
            IEnumerable<IBindableBlobPath> outputs, IBlobTimestampReader timestampReader)
        {
            // Avoid unnecessary network calls for non-matches. First, check to see if the blob matches this trigger.
            IReadOnlyDictionary<string, object> bindingData = input.CreateBindingData(possibleTrigger.ToBlobPath());

            if (bindingData == null)
            {
                // Blob is not a match for this trigger.
                return false;
            }

            // Next, check to see if the blob currently exists.
            DateTime? possibleInputTimestamp = timestampReader.GetLastModifiedTimestamp(possibleTrigger);

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
                bool allOutputsAreNewerThanInput = outputs.All(
                    o => IsOutputNewerThan(inputTimestamp, client, bindingData, o, timestampReader));

                if (allOutputsAreNewerThanInput)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsOutputNewerThan(DateTime inputTimestamp, CloudBlobClient client,
            IReadOnlyDictionary<string, object> bindingData, IBindableBlobPath output,
            IBlobTimestampReader timestampReader)
        {
            BlobPath outputPath = output.Bind(bindingData);

            // Assumes inputs and outputs are in the same storage account.
            CloudBlobContainer outputContainer = client.GetContainerReference(outputPath.ContainerName);
            CloudBlockBlob outputBlob = outputContainer.GetBlockBlobReference(outputPath.BlobName);
            DateTime? possibleOutputTimestamp = timestampReader.GetLastModifiedTimestamp(outputBlob);

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
