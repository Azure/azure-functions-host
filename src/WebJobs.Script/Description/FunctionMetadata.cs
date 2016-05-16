// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class FunctionMetadata
    {
        public FunctionMetadata()
        {
            Bindings = new Collection<BindingMetadata>();
        }

        public string Name { get; set; }

        /// <summary>
        /// The primary entry point for the function (to disambiguate if there are multiple
        /// scripts in the function directory).
        /// </summary>
        public string ScriptFile { get; set; }

        public ScriptType ScriptType { get; set; }

        public bool IsDisabled { get; set; }

        public Collection<BindingMetadata> Bindings { get; private set; }

        public IEnumerable<BindingMetadata> InputBindings
        {
            get
            {
                return Bindings.Where(p => p.Direction != BindingDirection.Out);
            }
        }

        public IEnumerable<BindingMetadata> OutputBindings
        {
            get
            {
                return Bindings.Where(p => p.Direction != BindingDirection.In);
            }
        }
    }
}
