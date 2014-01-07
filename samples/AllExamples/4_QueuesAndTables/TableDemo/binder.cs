using System;
using SimpleBatch;


public partial class TableFuncs2
{
    public static void Initialize(IConfiguration config)
    {
        config.TableBinders.Add(new TableBinderProvider());
    }
}

// Convert from guidkey --> intkey via azure lookup table
// - enforces proper partition and row key schema
// - encapsulates lookup property name 
// - could enforce any other lookup logic (string conversion, deserialization, etc)
class LookupImpl : ILookup
{
    public IAzureTableReader _table;

    //public LookupImpl(AzureTable
    public int Lookup(Guid guid)
    {
        string val;
        var d = _table.Lookup(
            partitionKey: "const", 
            rowKey: guid.ToString());
        
        if (d != null)
        {
            if (d.TryGetValue("intkey", out val))
            {
                int number;
                if (int.TryParse(val, out number))
                {
                    return number;
                }
            }
        }
        return -1;
    }
}

// Get an azure table and wrap it in a strongly typed C# class, LookupImpl.
class TableBinderProvider : ICloudTableBinderProvider
{
    static TableBinder _singleton = new TableBinder();

    class TableBinder : ICloudTableBinder
    {
        // TODO: IBinderEx and IBinder will get merged. IBinderEx returns BindResult<T> whereas IBinder just returns T. 
        public BindResult Bind(IBinderEx bindingContext, Type targetType, string tableName)
        {
            var innerTable = bindingContext.Bind<IAzureTableReader>(new TableAttribute(tableName));

            return new BindResult<ILookup>(new LookupImpl { _table = innerTable.Result }, innerTable);
        }
    }

    public ICloudTableBinder TryGetBinder(Type targetType, bool isReadOnly)
    {
        return (targetType == typeof(ILookup)) ? _singleton : null;
    }
}
