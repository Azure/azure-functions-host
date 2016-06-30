using System;

namespace WebJobs.Script.ConsoleHost.Cli.Types
{
    [AttributeUsage(AttributeTargets.Class)]
    public class VerbNameAttribute : Attribute
    {
        public string Name { get; }

        public VerbNameAttribute(string name)
        {
            Name = name;
        }
    }
}