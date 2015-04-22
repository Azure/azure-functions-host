// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Microsoft.Azure.WebJobs.Host.Bindings.Path
{
    /// <summary>
    /// Binding template providing functionality of capturing parameter values out of actual path matching  
    /// original input pattern.
    /// </summary>
    [DebuggerDisplay("{Pattern,nq}")]
    internal class BindingTemplateSource
    {
        private readonly string _pattern;
        private readonly Regex _captureRegex;

        public BindingTemplateSource(string pattern, Regex captureRegex)
        {
            if (pattern == null)
            {
                throw new ArgumentNullException("pattern");
            }

            if (captureRegex == null)
            {
                throw new ArgumentNullException("captureRegex");
            }

            _pattern = pattern;
            _captureRegex = captureRegex;
        }

        public string Pattern
        {
            get { return _pattern; }
        }

        public IEnumerable<string> ParameterNames
        {
            get
            {
                const string EntirePatternGroupName = "0";
                return _captureRegex.GetGroupNames().Where(n => !String.Equals(n, EntirePatternGroupName));
            }
        }

        /// <summary>
        /// Convenient factory method to parse input pattern, build capturing expression and instantiate
        /// binding template instance.
        /// </summary>
        /// <param name="input">A binding template string in a format supported by <see cref="BindingTemplateParser"/>.
        /// </param>
        /// <returns>Valid ready-to-use instance of <see cref="BindingTemplateSource"/>.</returns>
        public static BindingTemplateSource FromString(string input)
        {
            IEnumerable<BindingTemplateToken> tokens = BindingTemplateParser.GetTokens(input);
            string capturePattern = BuildCapturePattern(tokens);
            return new BindingTemplateSource(input, new Regex(capturePattern, RegexOptions.Compiled));
        }

        /// <summary>
        /// Utility method to build regexp to capture parameter values out of pre-parsed template tokens.
        /// </summary>
        /// <param name="tokens">Template tokens as generated and validated by 
        /// the <see cref="BindingTemplateParser"/>.</param>
        /// <returns>Regex pattern to capture parameter values, containing named capturing groups, matching
        /// structure and parameter names provided by the list of tokens.</returns>
        public static string BuildCapturePattern(IEnumerable<BindingTemplateToken> tokens)
        {
            StringBuilder builder = new StringBuilder("^");

            foreach (BindingTemplateToken token in tokens)
            {
                if (token.IsParameter)
                {
                    builder.Append(String.Format("(?<{0}>.*)", token.Value));
                }
                else
                {
                    builder.Append(Regex.Escape(token.Value));
                }
            }

            return builder.Append("$").ToString();
        }

        /// <summary>
        /// Retrieves parameter values out of the actual path if it matches the binding template pattern.
        /// </summary>
        /// <param name="actualPath">Path string to match</param>
        /// <returns>Dictionary of parameter names to parameter values, or null if no match.</returns>
        public IReadOnlyDictionary<string, object> CreateBindingData(string actualPath)
        {
            Match match = _captureRegex.Match(actualPath);
            if (!match.Success)
            {
                return null;
            }

            Dictionary<string, object> namedParameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            foreach (var parameterName in ParameterNames)
            {
                Debug.Assert(match.Groups[parameterName].Success, 
                    "Capturing pattern shouldn't allow unmatched named parameter groups!");
                namedParameters[parameterName] = match.Groups[parameterName].Value;
            }

            return namedParameters;
        }

        public override string ToString()
        {
            return _pattern;
        }
    }
}
