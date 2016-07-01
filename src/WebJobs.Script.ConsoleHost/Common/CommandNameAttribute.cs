using System;

namespace WebJobs.Script.ConsoleHost.Common
{
    [AttributeUsage(AttributeTargets.Class)]
    public class CommandNameAttribute : Attribute
    {
        public string Name { get; }

        public CommandNameAttribute(string name)
        {
            Name = name;
        }
    }
}