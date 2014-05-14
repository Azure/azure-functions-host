using System;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs
{
    // Represents binding to a named parameter from the named parameter dictionary. 
    // Dictionary could be populated via route parameters in a Blob pattern match, explicitly by user, or somewhere else.
    internal class NameParameterStaticBinding : ParameterStaticBinding
    {
        public override ParameterRuntimeBinding Bind(IRuntimeBindingInputs inputs)
        {
            string value;
            if (inputs.NameParameters != null)
            {
                if (inputs.NameParameters.TryGetValue(Name, out value))
                {
                    return new LiteralStringParameterRuntimeBinding { Name = Name, Value = value };
                }
            }
            throw new InvalidOperationException(string.Format("Can't bind keyname '{0}'", Name));
        }

        public override ParameterRuntimeBinding BindFromInvokeString(IRuntimeBindingInputs inputs, string invokeString)
        {
            return new LiteralStringParameterRuntimeBinding { Name = Name, Value = invokeString };
        }

        public override string Description
        {
            get
            {
                return string.Format("mapped from keyname '{0}'", "{" + Name + "}");
            }
        }

        public override string Prompt
        {
            get
            {
                return "Enter the value";
            }
        }

        public override string DefaultValue
        {
            get { return null; }
        }

        public override ParameterDescriptor ToParameterDescriptor()
        {
            return new RouteParameterDescriptor();
        }
    }
}
