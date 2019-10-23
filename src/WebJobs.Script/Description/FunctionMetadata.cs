// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class FunctionMetadata
    {
        private bool? _isHttpInOut = null;

        public FunctionMetadata()
        {
            Bindings = new Collection<BindingMetadata>();
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
        /// Gets or sets a value indicating whether the function is disabled.
        /// <remarks>
        /// A disabled function is still compiled and loaded into the host, but it will not
        /// be triggered automatically, and is not publicly addressable (except via admin invoke requests).
        /// </remarks>
        /// </summary>
        public bool IsDisabled { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether that this function is a direct invoke.
        /// </summary>
        public bool IsDirect { get; set; }

        public string FunctionId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Gets or sets a value indicating whether this function is a wrapper for Azure Function Proxy
        /// </summary>
        public bool IsProxy { get; set; }

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
    }
}
