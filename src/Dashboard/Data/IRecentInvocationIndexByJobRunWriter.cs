// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

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
