// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.Jobs.Host.Listeners;
using Microsoft.Azure.Jobs.Host.Timers;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs.Listeners
{
    internal interface IBlobNotificationStrategy : IIntervalSeparationCommand, IBlobWrittenWatcher
    {
        void Register(CloudBlobContainer container, ITriggerExecutor<ICloudBlob> triggerExecutor);
    }
}
