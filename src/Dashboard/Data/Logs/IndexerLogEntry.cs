// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Dashboard.Data.Logs
{
    public class IndexerLogEntry 
    {
        public string Id { get; set; }

        public DateTime Date { get; set; }

        public string Title { get; set; }

        public string ExceptionDetails { get; set; }
    }
}
