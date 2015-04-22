// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Dashboard.Data
{
    public interface IAbortRequestLogger
    {
        void LogAbortRequest(string queueName);

        bool HasRequestedAbort(string queueName);
    }
}
