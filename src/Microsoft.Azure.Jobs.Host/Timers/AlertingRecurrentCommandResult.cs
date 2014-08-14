// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;

namespace Microsoft.Azure.Jobs.Host.Timers
{
    internal struct AlertingRecurrentCommandResult
    {
        private readonly bool _succeeded;
        private readonly Task _stopWaiting;

        public AlertingRecurrentCommandResult(bool succeeded, Task stopWaiting)
        {
            _succeeded = succeeded;
            _stopWaiting = stopWaiting;
        }

        public bool Succeeded
        {
            get { return _succeeded; }
        }

        public Task StopWaiting
        {
            get { return _stopWaiting; }
        }
    }
}
