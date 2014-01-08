using Microsoft.WindowsAzure.Jobs;

namespace Dashboard.ViewModels
{
    public class FunctionLocationViewModel
    {
        internal FunctionLocation UnderlyingObject { get; private set; }

        internal FunctionLocationViewModel(FunctionLocation underlyingObject)
        {
            UnderlyingObject = underlyingObject;
        }

        public string GetId()
        {
            return UnderlyingObject.GetId();
        }

        public string GetShortName()
        {
            return UnderlyingObject.GetShortName();
        }

        public override string ToString()
        {
            return UnderlyingObject.ToString();
        }
    }
}
