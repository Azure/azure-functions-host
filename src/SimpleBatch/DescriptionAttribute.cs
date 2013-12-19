using System;
using System.Reflection;

namespace Microsoft.WindowsAzure.Jobs
{
    [AttributeUsage(AttributeTargets.Method)]
    public class DescriptionAttribute : Attribute
    {
        public string Description { get; set; }

        public DescriptionAttribute(string description)
        {
            this.Description = description;
        }

        public static DescriptionAttribute Build(CustomAttributeData attr)
        {
            if (attr.Constructor.DeclaringType.FullName != typeof(DescriptionAttribute).FullName)
            {
                return null;
            }
            string arg = (string)attr.ConstructorArguments[0].Value;
            return new DescriptionAttribute(arg);        
        }    
    }
}
