// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public enum BindingType
    {
        Queue,
        QueueTrigger,
        EventHub,
        EventHubTrigger,
        Blob,
        BlobTrigger,
        ServiceBus,
        ServiceBusTrigger,
        HttpTrigger,
        Http,
        Table,
        ManualTrigger,
        TimerTrigger,
        EasyTable
    }
}
