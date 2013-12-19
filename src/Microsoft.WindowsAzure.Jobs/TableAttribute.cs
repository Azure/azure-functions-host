using System;
using System.Reflection;

namespace Microsoft.WindowsAzure.Jobs
{
    // Type binds to an Azure table of the given name. 
    [AttributeUsage(AttributeTargets.Parameter)]
    public class TableAttribute : Attribute
    {
        // If empty, infer from the name of the local 
        // Beware of table name restrictions.
        public string TableName { get; set; }

        public TableAttribute(string tableName)
        {
            this.TableName = tableName;
        }

        public static TableAttribute Build(CustomAttributeData attr)
        {
            if (attr.Constructor.DeclaringType.FullName != typeof(TableAttribute).FullName)
            {
                return null;
            }
            string arg = (string)attr.ConstructorArguments[0].Value;
            return new TableAttribute(arg);
        }

        public override string ToString()
        {
            return string.Format("[Table{0})]", TableName);
        }
    }
}
