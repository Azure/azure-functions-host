// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.Azure.Jobs.Host.Timers
{
    internal struct RecurrentCommandResult
    {
        private readonly bool _succeeded;

        public RecurrentCommandResult(bool succeeded)
        {
            _succeeded = succeeded;
        }

        public bool Succeeded
        {
            get { return _succeeded; }
        }
    }
}
