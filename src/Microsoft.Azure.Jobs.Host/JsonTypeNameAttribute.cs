using System;

namespace Microsoft.Azure.Jobs.Host
{
    /// <summary>
    /// Provides a key to use in place of the .NET type name when deserializing polymophic objects using
    /// <see cref="PolymorphicJsonConverter"/>.
    /// </summary>
    internal class JsonTypeNameAttribute : Attribute
    {
        private readonly string _typeName;

        public JsonTypeNameAttribute(string typeName)
        {
            if (typeName == null)
            {
                throw new ArgumentNullException("typeName");
            }

            _typeName = typeName;
        }

        public string TypeName
        {
            get { return _typeName; }
        }
    }
}
