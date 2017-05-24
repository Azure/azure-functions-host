// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace Microsoft.Azure.WebJobs.Script.Description.Node.TypeScript
{
    [DebuggerDisplay("{DebuggerDisplay, nq}")]
    public class TypeScriptCompilationOptions
    {
        private readonly Dictionary<string, CompilationOption> _options = new Dictionary<string, CompilationOption>();

        public TypeScriptCompilationOptions()
        {
            // Default target to ES2015
            Target = "ES2015";

            // To make sure this works with Node, use commonjs as the module 'kind'
            Module = "commonjs";
        }

        public string ToolPath { get; set; }

        public string Module
        {
            get => GetOption<StringCompilationOption>()?.Value;
            internal set => SetOption(value);
        }

        public string Target
        {
            get => GetOption<StringCompilationOption>()?.Value;
            internal set => SetOption(value);
        }

        public string OutDir
        {
            get => GetOption<StringCompilationOption>()?.Value;
            internal set => SetOption(value);
        }

        public string RootDir
        {
            get => GetOption<StringCompilationOption>()?.Value;
            internal set => SetOption(value);
        }

        private string DebuggerDisplay => ToArgumentString("<filename>");

        private static string GetOptionName(string name)
        {
            return name?.ToLowerInvariant();
        }

        private void SetOption(string value, [CallerMemberName] string name = null)
        {
            name = GetOptionName(name);

            if (string.IsNullOrEmpty(value))
            {
                RemoveOption(name);
            }
            else
            {
                _options[name] = new StringCompilationOption(name, value);
            }
        }

        private void RemoveOption([CallerMemberName] string name = null)
        {
            name = GetOptionName(name);

            _options.Remove(name);
        }

        private T GetOption<T>([CallerMemberName] string name = null) where T : CompilationOption
        {
            name = GetOptionName(name);

            _options.TryGetValue(name, out CompilationOption outDir);

            return outDir as T;
        }

        public string ToArgumentString(string input)
        {
            string arguments = _options.Aggregate(new StringBuilder(), (r, o) => r.Append(o.Value + " "), r => r.ToString().Trim());

            return $"{input} {arguments}";
        }
    }
}
