using System;
using System.Reflection;

namespace Microsoft.Azure.Jobs
{
    // Represents binding to a named parameter from the named parameter dictionary. 
    // Dictionary could be populated via route parameters in a Blob pattern match, explicitly by user, or somewhere else.
    internal class NameParameterStaticBinding : ParameterStaticBinding
    {
        public string KeyName { get; set; }
        public bool UserSupplied { get; set; }

        public override void Validate(IConfiguration config, ParameterInfo parameter)
        {
            if (UserSupplied)
            {
                // $$$ This is a little ambiguous. This could be a runtime-supplied value, or it could be a type we model bind. 
                if (ObjectBinderHelpers.UseToStringParser(parameter.ParameterType))
                {
                    // If bindable from a string (ie, basically simple types like int and string), 
                    // then assume it's supplied at runtime by the user.  
                    return;
                }

                // Verify that a binder exists. 
                var binder = UnknownParameterRuntimeBinding.GetBinderOrThrow(config, parameter);

                var verify = binder as ICloudBinderVerify;
                if (verify != null)
                {
                    verify.Validate(parameter);
                }
            }
        }

        public override ParameterRuntimeBinding Bind(IRuntimeBindingInputs inputs)
        {
            string value;
            if (inputs.NameParameters != null)
            {
                if (inputs.NameParameters.TryGetValue(KeyName, out value))
                {
                    return new LiteralStringParameterRuntimeBinding { Value = value };
                }
            }
            if (UserSupplied)
            {
                // Not found. Do late time binding. 
                return new UnknownParameterRuntimeBinding { AccountConnectionString = inputs.AccountConnectionString };
            }
            throw new InvalidOperationException(string.Format("Can't bind keyname '{0}'", KeyName));
        }

        public override ParameterRuntimeBinding BindFromInvokeString(IRuntimeBindingInputs inputs, string invokeString)
        {
            if (UserSupplied)
            {
                // We don't know whether to use the invoke string or not until we have the target type. Let the runtime
                // binding decide which way to bind.
                return new UnknownInvokeParameterRuntimeBinding
                {
                    Value = invokeString,
                    AccountConnectionString = inputs.AccountConnectionString
                };
            }
            return new LiteralStringParameterRuntimeBinding { Value = invokeString };
        }

        public override string Description
        {
            get
            {
                if (UserSupplied)
                {
                    return "model bound.";
                }
                return string.Format("mapped from keyname '{0}'", "{" + KeyName + "}");
            }
        }

        public override string Prompt
        {
            get
            {
                return "Enter the value (if any)";
            }
        }

        public override string DefaultValue
        {
            get { return null; }
        }
    }
}
