// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Microsoft.Azure.WebJobs.Host.Bindings.Path
{
    /// <summary>
    /// A binding template class providing method of resolving parameterized template into a string by replacing
    /// template parameters with parameter values.
    /// </summary>
    [DebuggerDisplay("{Pattern,nq}")]
    internal class BindingTemplate
    {
        private readonly string _pattern;
        private readonly IReadOnlyList<BindingTemplateToken> _tokens;

        public BindingTemplate(string pattern, IReadOnlyList<BindingTemplateToken> tokens)
        {
            if (pattern == null)
            {
                throw new ArgumentNullException("pattern");
            }

            if (tokens == null)
            {
                throw new ArgumentNullException("tokens");
            }

            _pattern = pattern;
            _tokens = tokens;
        }

        public string Pattern
        {
            get { return _pattern; }
        }

        public IEnumerable<BindingTemplateToken> Tokens
        {
            get { return _tokens; }
        }

        public IEnumerable<string> ParameterNames
        {
            get
            {
                return from token in Tokens where token.IsParameter select token.Value;
            }
        }

        /// <summary>
        /// A factory method to parse input template string and construct a binding template instance using
        /// parsed tokens sequence.
        /// </summary>
        /// <param name="input">A binding template string in a format supported by <see cref="BindingTemplateParser"/>.
        /// </param>
        /// <returns>Valid ready-to-use instance of <see cref="BindingTemplate"/>.</returns>
        public static BindingTemplate FromString(string input)
        {
            IReadOnlyList<BindingTemplateToken> tokens = BindingTemplateParser.ParseTemplate(input);
            return new BindingTemplate(input, tokens);
        }

        /// <summary>
        /// Resolves original parameterized template into a string by replacing parameters with values provided as
        /// a dictionary.
        /// </summary>
        /// <param name="parameters">Dictionary providing parameter values.</param>
        /// <returns>Resolved string if succeeded.</returns>
        /// <exception cref="InvalidOperationException">Thrown when required parameter value is not available.
        /// </exception>
        public string Bind(IReadOnlyDictionary<string, string> parameters)
        {
            StringBuilder builder = new StringBuilder();

            foreach (BindingTemplateToken token in Tokens)
            {
                if (token.IsParameter)
                {
                    string value;
                    if (parameters != null && parameters.TryGetValue(token.Value, out value))
                    {
                        builder.Append(value);
                    }
                    else
                    {
                        throw new InvalidOperationException(String.Format("No value for named parameter '{0}'.",
                            token.Value));
                    }
                }
                else
                {
                    builder.Append(token.Value);
                }
            }

            return builder.ToString();
        }

        public override string ToString()
        {
            return _pattern;
        }
    }
}
