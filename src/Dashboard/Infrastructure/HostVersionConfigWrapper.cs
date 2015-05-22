// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;

namespace Dashboard
{
    public static class HostVersionConfigWrapper
    {
        public static bool HasWarning
        {
            get { return HostVersionConfig.HasWarning; }
        }

        public static IEnumerable<HostVersionModel> Warnings
        {
            get { return HostVersionConfig.Warnings.Select(w => new HostVersionModel(w.Label, w.Link)); }
        }
    }
}
