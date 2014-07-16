// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host.Bindings
{
    internal class ImmutableWatcher : IWatcher
    {
        private readonly ParameterLog _status;

        public ImmutableWatcher(ParameterLog status)
        {
            _status = status;
        }

        public ParameterLog GetStatus()
        {
            return _status;
        }
    }
}
