// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using Microsoft.Azure.WebJobs.Host.Bindings.Runtime;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    public class BindingContext
    {
        public IBinderEx Binder { get; set; }

        public object Input { get; set; }

        public Stream Value { get; set; }
        
        public IReadOnlyDictionary<string, string> BindingData { get; set; }
    }
}
