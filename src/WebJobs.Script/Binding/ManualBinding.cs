// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection.Emit;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    public class ManualBinding : FunctionBinding
    {
        public ManualBinding(ScriptHostConfiguration config, BindingMetadata metadata, FileAccess access) : base (config, metadata, access)
        {
        }

        public override Task BindAsync(BindingContext context)
        {
            throw new NotImplementedException();
        }

        public override Collection<CustomAttributeBuilder> GetCustomAttributes(Type parameterType)
        {
            return new Collection<CustomAttributeBuilder>();
        }
    }
}
