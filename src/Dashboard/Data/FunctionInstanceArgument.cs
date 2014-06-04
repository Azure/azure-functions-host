using System;

namespace Dashboard.Data
{
    public class FunctionInstanceArgument
    {
        private readonly FunctionArgumentEntity _argumentEntity;

        [CLSCompliant(false)]
        public FunctionInstanceArgument(FunctionArgumentEntity argumentEntity)
        {
            if (argumentEntity == null)
            {
                throw new ArgumentNullException("argumentEntity");
            }

            _argumentEntity = argumentEntity;
        }

        public string Value
        {
            get { return _argumentEntity.Value; }
        }

        public bool IsBlob
        {
            get { return _argumentEntity.IsBlob; }
        }
    }
}
