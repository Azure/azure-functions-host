using System;
using System.Data.Services;
using System.Data.Services.Client;
using System.Diagnostics;
using System.Linq;
using Microsoft.WindowsAzure.StorageClient;

namespace Microsoft.WindowsAzure.Jobs
{
    internal class HostTable : IHostTable
    {
        private readonly CloudStorageAccount _account;
        private readonly string _tableName;

        public HostTable(CloudStorageAccount account, string tableName)
        {
            if (account == null)
            {
                throw new ArgumentNullException("account");
            }

            if (tableName == null)
            {
                throw new ArgumentNullException("tableName");
            }

            _account = account;
            _tableName = tableName;
        }

        public Guid GetOrCreateHostId(string hostName)
        {
            CloudTableClient client = _account.CreateCloudTableClient();
            Debug.Assert(client != null);
            client.CreateTableIfNotExist(_tableName);
            TableServiceContext context = client.GetDataServiceContext();
            Debug.Assert(context != null);
            Guid possibleId = Guid.NewGuid();
            HostEntity newEntity = new HostEntity
            {
                PartitionKey = hostName,
                RowKey = String.Empty,
                Id = possibleId
            };
            context.AddObject(_tableName, newEntity);

            Guid id;

            try
            {
                // Try creating a new ID for this host name.
                context.SaveChanges();
                id = possibleId;
            }
            catch (DataServiceRequestException exception)
            {
                Debug.Assert(exception != null);

                if (exception.Response != null)
                {
                    OperationResponse operationResponse = exception.Response.FirstOrDefault();

                    if (operationResponse == null || operationResponse.StatusCode != 409)
                    {
                        throw;
                    }
                }

                // If an ID already exists, use that one instead.
                id = GetExistingHostId(client, hostName);
            }

            return id;
        }

        private Guid GetExistingHostId(CloudTableClient client, string hostName)
        {
            Debug.Assert(client != null);
            TableServiceContext context = client.GetDataServiceContext();
            Debug.Assert(context != null);
            IQueryable<HostEntity> queryable = context.CreateQuery<HostEntity>(_tableName);
            Debug.Assert(queryable != null);
            HostEntity existingEntity = TableQueryable.GetEntity(queryable, hostName, String.Empty);
            Debug.Assert(existingEntity != null);
            return existingEntity.Id;
        }
    }

    internal class HostEntity : TableServiceEntity
    {
        public Guid Id { get; set; }
    }
}
