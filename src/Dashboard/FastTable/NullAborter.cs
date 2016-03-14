// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Dashboard.HostMessaging;

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