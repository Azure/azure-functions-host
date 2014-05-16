using System;

namespace Dashboard.Data
{
    internal class FunctionIdentifier
    {
        private readonly Guid _hostId;
        private readonly string _hostFunctionId;

        public FunctionIdentifier(Guid hostId, string hostFunctionId)
        {
            _hostId = hostId;
            _hostFunctionId = hostFunctionId;
        }

        public Guid HostId
        {
            get { return _hostId; }
        }

        public string HostFunctionId
        {
            get { return _hostFunctionId; }
        }

        public static FunctionIdentifier Parse(string functionId)
        {
            int underscoreIndex = functionId.IndexOf('_');
            string hostIdPortion = functionId.Substring(0, underscoreIndex);
            Guid hostId = Guid.Parse(hostIdPortion);
            string hostFunctionId = functionId.Substring(underscoreIndex + 1);
            return new FunctionIdentifier(hostId, hostFunctionId);
        }

        public override string ToString()
        {
            return _hostId.ToString() + "_" + _hostFunctionId;
        }
    }
}
