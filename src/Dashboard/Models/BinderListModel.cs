using Dashboard.Models.Protocol;

namespace Dashboard.Controllers
{
    public class BinderListModel
    {
        public Entry[] Binders { get; set; }

        public class Entry
        {
            public string TypeName { get; set; } // type this binder applies to

            public string AccountName { get; set; }

            public CloudBlobPathModel Path { get; set; }

            public string EntryPoint { get; set; }
        }
    }
}
