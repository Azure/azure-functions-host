// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Protocols;
using Microsoft.WindowsAzure.Storage.Blob;
using Dashboard.HostMessaging;
using Dashboard.Data.Logs;

namespace Dashboard.Data
{
    internal class NullAborter : IAborter
    {
        bool IAborter.HasRequestedHostInstanceAbort(string queueName)
        {
            return false;
        }

        void IAborter.RequestHostInstanceAbort(string queueName)
        {

        }
    }
}