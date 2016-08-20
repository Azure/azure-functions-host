using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace NCli
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1019:DefineAccessorsForAttributeArguments")]
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class VerbAttribute : Attribute
    {
        public string HelpText { get; set; }

        public string Usage { get; set; }

        public bool ShowInHelp { get; set; } = true;

        public object Scope { get; set; }

        public bool AllowEmpty { get; set; }

        public IEnumerable<string> Names { get; private set; }

        public VerbAttribute(params string[] names)
        {
            Names = names?.Select(n => n.ToLowerInvariant()) ?? Enumerable.Empty<string>();
        }
    }
}
