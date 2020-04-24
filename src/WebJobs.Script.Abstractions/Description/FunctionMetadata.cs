// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace Microsoft.Azure.WebJobs.Script.Abstractions.Description
{
    public class FunctionMetadata
    {
        private bool? _isHttpInOut = null;

        public FunctionMetadata()
        {
            Bindings = new Collection<BindingMetadata>();
            Properties = new Dictionary<string, object>();
        }

        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the primary entry point for the function (to disambiguate if there are multiple
        /// scripts in the function directory).
        /// </summary>
        public string ScriptFile { get; set; }

        /// <summary>
        /// Gets or sets the function root directory.
        /// </summary>
        public string FunctionDirectory { get; set; }

        /// <summary>
        /// Gets or sets the optional named entry point for a function.
        /// </summary>
        public string EntryPoint { get; set; }

        public string Language { get; set; }

        /// <summary>
        /// Gets all the properties tagged to this instance.
        /// </summary>
        public IDictionary<string, object> Properties { get; }

        public string FunctionId { get; set; } = Guid.NewGuid().ToString();

        public Collection<BindingMetadata> Bindings { get; }

        public bool IsHttpInAndOutFunction
        {
            get
            {
                if (_isHttpInOut == null)
                {
                    if (InputBindings.Count() != 1 || OutputBindings.Count() != 1)
                    {
                        _isHttpInOut = false;
                        return false;
                    }

                    BindingMetadata inputBindingMetadata = InputBindings.ElementAt(0);
                    BindingMetadata outputBindingMetadata = OutputBindings.ElementAt(0);
                    if (string.Equals("httptrigger", inputBindingMetadata.Type, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals("http", outputBindingMetadata.Type, StringComparison.OrdinalIgnoreCase))
                    {
                        _isHttpInOut = true;
                        return true;
                    }
                    _isHttpInOut = false;
                    return false;
                }

                return _isHttpInOut.Value;
            }
        }

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

        public BindingMetadata Trigger
        {
            get { return InputBindings.FirstOrDefault(binding => binding.IsTrigger); }
        }
    }
}
