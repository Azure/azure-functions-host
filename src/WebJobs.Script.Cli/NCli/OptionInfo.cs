using System;
using System.Reflection;

namespace NCli
{
    internal class OptionInfo
    {
        public PropertyInfo PropertyInfo { get; set; }
        public OptionAttribute Attribute { get; set; }
    }
}
