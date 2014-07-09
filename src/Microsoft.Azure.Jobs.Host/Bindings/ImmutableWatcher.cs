using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host.Bindings
{
    internal class ImmutableWatcher : IWatcher
    {
        private readonly ParameterLog _status;

        public ImmutableWatcher(ParameterLog status)
        {
            _status = status;
        }

        public ParameterLog GetStatus()
        {
            return _status;
        }
    }
}
