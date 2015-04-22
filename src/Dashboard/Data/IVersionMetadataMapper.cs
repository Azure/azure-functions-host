// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Dashboard.Data
{
    public interface IVersionMetadataMapper
    {
        DateTimeOffset GetVersion(IDictionary<string, string> metadata);

        void SetVersion(DateTimeOffset version, IDictionary<string, string> metadata);
    }
}
