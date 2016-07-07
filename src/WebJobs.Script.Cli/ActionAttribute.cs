using System;

namespace WebJobs.Script.Cli
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    sealed class ActionAttribute : Attribute
    {
        public Context Context { get; set; }

        public Context SubContext { get; set; }

        public string Name { get; set; }

        public string HelpText { get; set; } = "placeholder";

        public bool ShowInHelp { get; set; } = true;
    }
}
