// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.ObjectModel;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class FunctionMetadata
    {
        public FunctionMetadata()
        {
            InputBindings = new Collection<BindingMetadata>();
            OutputBindings = new Collection<BindingMetadata>();
        }

        public string Name { get; set; }

        public string Source { get; set; }

        public bool IsDisabled { get; set; }

        public Collection<BindingMetadata> InputBindings { get; private set; }

        public Collection<BindingMetadata> OutputBindings { get; private set; }
    }
}
