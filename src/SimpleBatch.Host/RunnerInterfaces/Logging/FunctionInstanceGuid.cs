using System;


namespace Microsoft.WindowsAzure.Jobs
{   
    // This is a helper class that provides a type-safe wrapper for GUIDs that represent Function instances.
    // $$$ Issues:
    // - Change to struct? Rename to FunctionIndexGuid so it's obvious it's a guid wrapper.
    //   (Ideally be a struct, but then value-type semantics make serialization unhappy.)
    // - Make sure this JSON serializes like a guid?
    // Entry in a secondary index that points back to the primary table.
    internal class FunctionInstanceGuid
    {
        private Guid _instance;

        public FunctionInstanceGuid()
        {
            _instance = Guid.Empty;
        }

        public FunctionInstanceGuid(Guid guid) 
        {
            _instance = guid;
        }
        public FunctionInstanceGuid(ExecutionInstanceLogEntity log)
        {
            _instance = log.FunctionInstance.Id;
        }

        public static implicit operator FunctionInstanceGuid(Guid guid)
        {
            return new FunctionInstanceGuid(guid);
        }

        public static implicit operator Guid(FunctionInstanceGuid obj)
        {
            return obj.Value;
        }

        // C# compiler won't let you set properties from the struct ctor, 
        // so need to use a field instead of an automatic property.
        public Guid Value
        {
            get
            {
                return _instance;
            }
            set
            {
                _instance = value;
            }
        }

        public override string ToString()
        {
            return _instance.ToString();
        }

        public override bool Equals(object obj)
        {
            return _instance.Equals(obj);
        }
        public override int GetHashCode()
        {
            return _instance.GetHashCode();
        }

        public static bool operator ==(FunctionInstanceGuid a, FunctionInstanceGuid b)
        {
            return a.Value == b.Value;
        }
        public static bool operator !=(FunctionInstanceGuid a, FunctionInstanceGuid b)
        {
            return a.Value != b.Value;
        }
    }
}