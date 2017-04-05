// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    [DebuggerDisplay("{Name} ({Type.FullName})")]
    public class ParameterDescriptor
    {
        public ParameterDescriptor(string name, Type type)
            : this(name, type, new Collection<CustomAttributeBuilder>())
        {
        }

        public ParameterDescriptor(string name, Type type, Collection<CustomAttributeBuilder> attributes)
        {
            Name = name;
            Type = type;
            Attributes = ParameterAttributes.None;
            CustomAttributes = attributes;
        }

        public string Name { get; private set; }

        public Type Type { get; private set; }

        public ParameterAttributes Attributes { get; set; }

        public bool IsTrigger { get; set; }

        public Collection<CustomAttributeBuilder> CustomAttributes { get; private set; }
    }
}
