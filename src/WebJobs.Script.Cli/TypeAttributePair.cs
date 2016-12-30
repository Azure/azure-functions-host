using System;
using System.Collections.Generic;

namespace WebJobs.Script.Cli
{
    internal class TypeAttributePair
    {
        public Type Type { get; set; }
        public ActionAttribute Attribute { get; set; }
    }
}