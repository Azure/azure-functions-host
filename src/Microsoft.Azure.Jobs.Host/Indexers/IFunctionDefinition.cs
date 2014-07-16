// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.Jobs.Host.Executors;
using Microsoft.Azure.Jobs.Host.Listeners;

namespace Microsoft.Azure.Jobs.Host.Indexers
{
    internal interface IFunctionDefinition
    {
        IFunctionInstanceFactory InstanceFactory { get; }

        IListenerFactory ListenerFactory { get; }
    }
}
