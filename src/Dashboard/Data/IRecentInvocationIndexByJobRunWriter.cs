// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Protocols;

namespace Dashboard.Data
{
    public interface IRecentInvocationIndexByJobRunWriter
    {
        void CreateOrUpdate(FunctionInstanceSnapshot snapshot, WebJobRunIdentifier webJobRunId, DateTimeOffset timestamp);

        void DeleteIfExists(WebJobRunIdentifier webJobRunId, DateTimeOffset timestamp, Guid id);
    }
}
