// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs.Bindings
{
    internal class BlobCommittedAction : IBlobCommitedAction
    {
        private readonly ICloudBlob _blob;
        private readonly Guid _functionInstanceId;
        private readonly IBlobWrittenWatcher _blobWrittenWatcher;

        public BlobCommittedAction(ICloudBlob blob, Guid functionInstanceId, IBlobWrittenWatcher blobWrittenWatcher)
        {
            _blob = blob;
            _functionInstanceId = functionInstanceId;
            _blobWrittenWatcher = blobWrittenWatcher;
        }

        public void Execute()
        {
            // This is the critical call to record causality. 
            // This must be called after the blob is written, since it may stamp the blob. 
            BlobCausalityManager.SetWriter(_blob, _functionInstanceId);

            // Notify that blob is available. 
            if (_blobWrittenWatcher != null)
            {
                _blobWrittenWatcher.Notify(_blob);
            }
        }
    }
}
