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
            get { return _argumentEntity.IsBlob.HasValue && _argumentEntity.IsBlob.Value; }
        }

        public bool IsBlobInput
        {
            get { return _argumentEntity.IsBlobInput.HasValue && _argumentEntity.IsBlobInput.Value; }
        }
    }
}
