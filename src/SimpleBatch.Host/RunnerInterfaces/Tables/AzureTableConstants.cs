using System.Xml.Linq;

namespace AzureTables
{
    // Constants for writing the XML schema for Azure Tables.
    class AzureTableConstants
    {
        public static XNamespace AtomNamespace = "http://www.w3.org/2005/Atom";
        public static XNamespace DataNamespace = "http://schemas.microsoft.com/ado/2007/08/dataservices";
        public static XNamespace MetadataNamespace = "http://schemas.microsoft.com/ado/2007/08/dataservices/metadata";
    }
}
