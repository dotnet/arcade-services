using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Maestro.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using NUnit.Framework;

namespace Maestro.Web.Tests
{
    [SetUpFixture]
    public static class SharedData
    {
        public static TestDatabase Database { get; private set; }

        [OneTimeSetUp]
        public static void SetUp()
        {
            Database = new SharedTestDatabase();
        }

        [OneTimeTearDown]
        public static void TearDown()
        {
            Database.Dispose();
            Database = null;
        }

        private class SharedTestDatabase : TestDatabase
        {
        }
    }

    public class TestDatabase : IDisposable
    {
        private const string TestDatabasePrefix = "TestFixtureDatabase_";
        private string _databaseName;
        private readonly SemaphoreSlim _createLock = new SemaphoreSlim(1);

        protected TestDatabase()
        {
        }

        public void Dispose()
        {
            using var connection = new SqlConnection("Data Source=localhost\\SQLEXPRESS;Initial Catalog=master;Integrated Security=true");
            connection.Open();
            DropAllTestDatabases(connection).GetAwaiter().GetResult();
        }

        public async Task<string> GetConnectionString()
        {
            if (_databaseName != null)
            {
                return ConnectionString;
            }

            await _createLock.WaitAsync();
            try
            {
                string databaseName = $"{TestDatabasePrefix}{DateTime.Now:yyyyMMddHHmmss}";
                await using (var connection = new SqlConnection("Data Source=localhost\\SQLEXPRESS;Initial Catalog=master;Integrated Security=true"))
                {
                    await connection.OpenAsync();

                    await DropAllTestDatabases(connection);

                    await using (SqlCommand createCommand = connection.CreateCommand())
                    {
                        createCommand.CommandText = $"CREATE DATABASE {databaseName}";
                        await createCommand.ExecuteNonQueryAsync();
                    }
                }

                var collection = new ServiceCollection();
                collection.AddSingleton<IHostEnvironment>(new HostingEnvironment
                    {EnvironmentName = Environments.Development});
                collection.AddBuildAssetRegistry(o =>
                {
                    o.UseSqlServer(GetConnectionString(databaseName));
                    o.EnableServiceProviderCaching(false);
                });

                await using ServiceProvider provider = collection.BuildServiceProvider();
                await provider.GetRequiredService<BuildAssetRegistryContext>().Database.MigrateAsync();

                _databaseName = databaseName;
                return ConnectionString;
            }
            finally
            {
                _createLock.Dispose();
            }
        }

        private async Task DropAllTestDatabases(SqlConnection connection)
        {
            var previousTestDbs = new List<string>();
            await using (SqlCommand command = connection.CreateCommand())
            {
                command.CommandText = $"SELECT name FROM sys.databases WHERE name LIKE '{TestDatabasePrefix}%'";
                await using SqlDataReader reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    previousTestDbs.Add(reader.GetString(0));
                }
            }

            foreach (string db in previousTestDbs)
            {
                TestContext.WriteLine($"Dropping test database '{db}'");
                await using SqlCommand command = connection.CreateCommand();
                command.CommandText = $"ALTER DATABASE {db} SET single_user with rollback immediate; DROP DATABASE {db}";
                await command.ExecuteNonQueryAsync();
            }
        }

        private string ConnectionString => GetConnectionString(_databaseName);

        private string GetConnectionString(string databaseName)
        {
            return $@"Data Source=localhost\SQLEXPRESS;Initial Catalog={databaseName};Integrated Security=true";
        }
    }
}
