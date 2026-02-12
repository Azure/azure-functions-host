// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.AppCapabilities
{
    public interface IAppCapabilitiesStore
    {
        public IReadOnlyDictionary<string, string> Capabilities { get; }

        public void Set(string key, string value);

        public void SetAll(IDictionary<string, string> capabilities);
    }
}
