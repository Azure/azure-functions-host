namespace Microsoft.Azure.Jobs
{
    internal class CloudBlobPathConverter : StringConverter<CloudBlobPath>
    {
        public override object ReadFromString(string value)
        {
            return new CloudBlobPath(value);
        }
    }
}
