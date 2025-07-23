using System;
using Microsoft.IdentityModel.Tokens;

namespace Dependency56
{
    public static class TypeGetter
    {
        public static Type ReturnSecurityKeyType()
        {
            return typeof(SecurityKey);
        }
    }
}
