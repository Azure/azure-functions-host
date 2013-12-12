using RunnerInterfaces;

namespace WebFrontEnd.Models.Protocol
{
    public class CloudBlobPathModel
    {
        internal CloudBlobPath UnderlyingObject { get; private set; }

        internal CloudBlobPathModel(CloudBlobPath underlyingObject)
        {
            UnderlyingObject = underlyingObject;
        }

        public override string ToString()
        {
            return UnderlyingObject.ToString();
        }
    }
}