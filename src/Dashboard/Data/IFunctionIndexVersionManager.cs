using System;

namespace Dashboard.Data
{
    internal interface IFunctionIndexVersionManager
    {
        void UpdateOrCreateIfLatest(DateTimeOffset version);
    }
}
