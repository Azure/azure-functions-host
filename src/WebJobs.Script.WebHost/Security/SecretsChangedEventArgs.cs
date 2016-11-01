// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class SecretsChangedEventArgs : EventArgs
    {
        public ScriptSecretsType SecretsType { get; set; }

        public string Name { get; set; }
    }
}