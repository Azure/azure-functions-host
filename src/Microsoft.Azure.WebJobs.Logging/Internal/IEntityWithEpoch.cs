// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Logging
{
    // Get the "epoch" that this TableEntity is associated with. 
    // Each epoch gets its own Azure Table. This is to cooperate with deleting. 
    // See https://azure.microsoft.com/en-us/documentation/articles/storage-table-design-guide/#high-volume-delete-pattern
    internal interface IEntityWithEpoch
    {
        DateTime GetEpoch();
    }
}