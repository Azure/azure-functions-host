// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Dashboard.Data.Logs
{
    public interface IIndexerLogWriter
    {
        void Write(IndexerLogEntry entry);
    }
}
