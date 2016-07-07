using System;
using System.Collections.Generic;

namespace WebJobs.Script.Cli
{
    internal class ActionType
    {
        public Type Type { get; set; }
        public IEnumerable<Context> Contexts { get; set; }
        public IEnumerable<Context> SubContexts { get; set; }
        public IEnumerable<string> Names { get; set; }
    }
}