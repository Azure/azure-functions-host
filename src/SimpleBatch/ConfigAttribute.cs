using System;

namespace Microsoft.WindowsAzure.Jobs
{
    // Specify this parameter comes from configuration 
    [AttributeUsage(AttributeTargets.Parameter)]
    internal class ConfigAttribute : Attribute
    {
        // Short name of the file containing the configuration. 
        public string Filename { get; set; }

        public ConfigAttribute()
        {
        }

        public ConfigAttribute(string filename)
        {
            this.Filename = filename;
        }
    }
}
