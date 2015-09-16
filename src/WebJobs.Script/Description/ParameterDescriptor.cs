// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Reflection.Emit;

namespace Microsoft.Azure.WebJobs.Script
{
    public class ParameterDescriptor
    {
        public string Name { get; set; }
        public Type Type { get; set; }
        public ParameterAttributes Attributes { get; set; }
        public Collection<CustomAttributeBuilder> CustomAttributes { get; set; }
    }
}
