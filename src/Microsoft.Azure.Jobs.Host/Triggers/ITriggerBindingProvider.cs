// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.Azure.Jobs.Host.Triggers
{
    internal interface ITriggerBindingProvider
    {
        ITriggerBinding TryCreate(TriggerBindingProviderContext context);
    }
}
