using System;

namespace WebJobs.Script.ConsoleHost.Common
{
    [AttributeUsage(AttributeTargets.Class)]
    public class CommandNamesAttribute : Attribute
    {
        public string[] Names { get; }

        public CommandNamesAttribute(params string[] names)
        {
            Names = names;
        }
    }
}