// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using Colors.Net;
using Dapper;
using NCli;
using WebJobs.Script.Cli.Common.Models;
using WebJobs.Script.Cli.Interfaces;

namespace WebJobs.Script.Cli.Common
{
    internal class TipsManager : ITipsManager
    {
        private static readonly string DbFile = 
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".azurefunctions", "history");

        private IVerb _verb;
        private readonly IDependencyResolver _dependencyResolver;

        public TipsManager(IDependencyResolver dependencyResolver)
        {
            _dependencyResolver = dependencyResolver;
        }

        private void EnsureDatabase()
        {
            if (_verb == null)
            {
                _verb = _dependencyResolver.GetService<IVerb>();
            }

            if (!FileSystemHelpers.FileExists(DbFile))
            {
                using (var connection = GetConnection())
                {
                    connection.Open();
                    connection.Execute(@"CREATE TABLE Invocation(
                                            Id         INTEGER PRIMARY KEY AUTOINCREMENT,
                                            Verb       varchar(100) not null,
                                            UserVerb   varchar(100) not null,
                                            Timestamp  datetime not null,
                                            Result     integer)");
                }
            }
        }

        public void Record(bool failed)
        {
            EnsureDatabase();
            using (var connection = GetConnection())
            {
                connection.Open();
                var invocation = new Invocation
                {
                    Verb = _verb.GetType().Name,
                    UserVerb = _verb.OriginalVerb ?? string.Empty,
                    Result = failed ? InvocationResult.Error : InvocationResult.Success,
                    Timestamp = DateTime.Now
                };

                connection.Query($@"INSERT INTO Invocation (Verb, UserVerb, Timestamp, Result) VALUES (@Verb, @UserVerb, @Timestamp, @Result)", invocation);
            }
        }

        public IEnumerable<Invocation> GetInvocations(int count)
        {
            EnsureDatabase();
            using (var connection = GetConnection())
            {
                return connection.Query<Invocation>($@"SELECT Id, Verb, Timestamp, Result FROM Invocation WHERE Verb = @verb ORDER BY Timestamp DESC LIMIT {count}", new { verb = _verb.GetType().Name });
            }
        }

        public IEnumerable<Invocation> GetAll()
        {
            EnsureDatabase();
            using (var connection = GetConnection())
            {
                return connection.Query<Invocation>($@"SELECT * FROM Invocation ORDER BY Timestamp DESC").ToList();
            }
        }

        public ITipsManager DisplayTip(string tip)
        {
            var invocations = GetInvocations(10);
            invocations = invocations.Where(i => i.Timestamp > DateTime.Now.AddMinutes(-5));
            if (invocations.Count() < 5)
            {
                ColoredConsole.WriteLine().WriteLine(tip);
            }
            return this;
        }

        private static SQLiteConnection GetConnection()
        {
            return new SQLiteConnection($"Data Source={DbFile}");
        }
    }
}
