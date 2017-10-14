// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;

namespace WebJobs.Script.Host.Standalone
{
    /*
    -- Stored procedure needs to be modified as shown below:
    
    ALTER PROCEDURE [function].[leases_tryAcquireOrRenew]
    @LeaseName NVARCHAR (127), @RequestorName NVARCHAR (127), @LeaseExpirationTimeSpan INT, @Metadata NVARCHAR(MAX) = NULL, @HasLease BIT OUTPUT
    AS
    BEGIN
        SET NOCOUNT ON;

        BEGIN TRANSACTION

        UPDATE  [function].[Leases] SET [LastRenewal] = CURRENT_TIMESTAMP, [LeaseExpirationTimeSpan] = @LeaseExpirationTimeSpan WHERE [LeaseName] = @LeaseName AND [RequestorName] = @RequestorName
        IF @@ROWCOUNT = 0
            INSERT INTO [function].[Leases] ([LeaseName], [RequestorName], [LeaseExpirationTimeSpan], [LastRenewal], [Metadata], [HasLease])
            VALUES (@LeaseName, @RequestorName, @LeaseExpirationTimeSpan, CURRENT_TIMESTAMP, @Metadata, 0)

        COMMIT TRANSACTION
    
        UPDATE [function].[Leases]
        SET [HasLease] = 0
        WHERE [LeaseName] = @LeaseName AND [HasLease] = 1 AND [RequestorName] <> @RequestorName AND DATEDIFF(SECOND, [LastRenewal], CURRENT_TIMESTAMP) > @LeaseExpirationTimeSpan

        BEGIN TRANSACTION
    
        IF NOT EXISTS (SELECT * FROM [function].[Leases] WHERE [LeaseName] = @LeaseName AND [HasLease] = 1)
            UPDATE [function].[Leases]
            SET [HasLease] = 1
            WHERE [LeaseName] = @LeaseName AND [RequestorName] = @RequestorName

        COMMIT TRANSACTION

        SELECT @HasLease = [HasLease] FROM [function].[Leases] WHERE [LeaseName] = @LeaseName AND [RequestorName] = @RequestorName
    END
    GO
    */

    internal class SqlLeaseDistributedLockManager : IDistributedLockManager
    {
        private const string OwnerKey = "_owner";

        private static readonly string InstanceId = Guid.NewGuid().ToString();

        public const string ConnectionStringName = "AzureWebJobsLease";

        public SqlLeaseDistributedLockManager()
        {
        }

        public async Task<IDistributedLock> TryLockAsync(string account, string lockId, string lockOwnerId,
            string proposedLeaseId, TimeSpan lockPeriod,
            CancellationToken cancellationToken)
        {
            var result = await TryAcquireOrRenewLeaseAsync(account, lockId, lockOwnerId, lockPeriod, cancellationToken);

            if (result)
            {
                return new SqlLockHandle(account, lockId, lockPeriod);
            }

            return null;
        }

        public async Task<bool> RenewAsync(IDistributedLock lockHandle, CancellationToken cancellationToken)
        {
            SqlLockHandle sqlLockHandle = (SqlLockHandle)lockHandle;
            return await TryAcquireOrRenewLeaseAsync(sqlLockHandle.Account, sqlLockHandle.LockId, null, sqlLockHandle.LockPeriod, cancellationToken);
        }

        public async Task<string> GetLockOwnerAsync(string account, string lockId, CancellationToken cancellationToken)
        {
            var metadata = await ReadMetadataAsync(account, lockId, cancellationToken);
            return metadata[OwnerKey];
        }

        public async Task ReleaseLockAsync(IDistributedLock lockHandle, CancellationToken cancellationToken)
        {
            SqlLockHandle sqlLockHandle = (SqlLockHandle)lockHandle;

            var connectionString = GetConnectionString(sqlLockHandle.Account);

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync(cancellationToken);

                using (SqlCommand cmd = connection.CreateCommand())
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.CommandText = "[function].[leases_release]";
                    cmd.Parameters.Add("@LeaseName", SqlDbType.NVarChar, 127).Value = sqlLockHandle.LockId;
                    cmd.Parameters.Add("@RequestorName", SqlDbType.NVarChar, 127).Value = InstanceId;

                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }
            }
        }

        private static async Task<bool> TryAcquireOrRenewLeaseAsync(string account, string lockId, string owner, TimeSpan lockPeriod,
            CancellationToken cancellationToken)
        {
            var connectionString = GetConnectionString(account);

            var metadata = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(owner))
            {
                metadata.Add(OwnerKey, owner);
            }

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync(cancellationToken);
                using (SqlCommand cmd = connection.CreateCommand())
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.CommandText = "[function].[leases_tryAcquireOrRenew]";
                    cmd.Parameters.Add("@LeaseName", SqlDbType.NVarChar, 127).Value = lockId;
                    cmd.Parameters.Add("@RequestorName", SqlDbType.NVarChar, 127).Value = InstanceId;
                    cmd.Parameters.Add("@LeaseExpirationTimeSpan", SqlDbType.Int).Value = (int)lockPeriod.TotalSeconds;
                    cmd.Parameters.Add("@Metadata", SqlDbType.NVarChar).Value = JsonConvert.SerializeObject(metadata);
                    cmd.Parameters.Add("@HasLease", SqlDbType.Bit).Direction = ParameterDirection.Output;

                    await cmd.ExecuteNonQueryAsync(cancellationToken);

                    return (bool)cmd.Parameters["@HasLease"].Value;
                }
            }
        }

        private async Task<Dictionary<string, string>> ReadMetadataAsync(string account, string lockId, CancellationToken cancellationToken)
        {
            var connectionString = GetConnectionString(account);

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync(cancellationToken);

                using (SqlCommand cmd = connection.CreateCommand())
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.CommandText = "[function].leases_getMetadata";
                    cmd.Parameters.Add("@LeaseName", SqlDbType.NVarChar, 127).Value = lockId;
                    cmd.Parameters.Add("@RequestorName", SqlDbType.NVarChar, 127).Value = InstanceId;
                    cmd.Parameters.Add("@Metadata", SqlDbType.NVarChar, -1).Direction = ParameterDirection.Output;
                    cmd.Parameters.Add("@HasLease", SqlDbType.Bit).Direction = ParameterDirection.Output;
                    await cmd.ExecuteNonQueryAsync(cancellationToken);

                    var hasLease = (bool)cmd.Parameters["@HasLease"].Value;

                    var serializedMetadata = (string)cmd.Parameters["@Metadata"].Value;

                    Dictionary<string, string> metadataDict;

                    if (string.IsNullOrEmpty(serializedMetadata))
                    {
                        metadataDict = new Dictionary<string, string>();
                    }
                    else
                    {
                        metadataDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(serializedMetadata);
                    }

                    // We don't want to fail even if the lease is not active
                    return metadataDict;
                }
            }
        }

        private static string GetConnectionString(string accountName)
        {
            if (string.IsNullOrWhiteSpace(accountName))
            {
                accountName = ConnectionStringName;
            }

            return AmbientConnectionStringProvider.Instance.GetConnectionString(accountName);
        }
    }

    internal class SqlLockHandle : IDistributedLock
    {
        public SqlLockHandle(string account, string lockId, TimeSpan lockPeriod)
        {
            Account = account;
            LockId = lockId;
            LockPeriod = lockPeriod;
        }

        public string Account { get; private set; }

        public string LockId { get; private set; }

        public TimeSpan LockPeriod { get; private set; }
    }
}
