using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.DncEng.CommandLineLib;

namespace Microsoft.DncEng.SecretManager.SecretTypes
{
    [Name("sql-connection-string")]
    public class SqlServerConnectionString : SecretType<SqlServerConnectionString.Parameters>
    {
        private readonly ISystemClock _clock;
        private readonly IConsole _console;

        public class Parameters
        {
            public string AdminConnectionName { get; set; }
            public string DataSource { get; set; }
            public string Database { get; set; }
            public string Permissions { get; set; }
        }

        public SqlServerConnectionString(ISystemClock clock, IConsole console)
        {
            _clock = clock;
            _console = console;
        }

        protected override async Task<SecretData> RotateValue(Parameters parameters, RotationContext context, CancellationToken cancellationToken)
        {
            string dataSource = parameters.DataSource;

            string adminConnectionString = await context.GetSecretValue(parameters.AdminConnectionName);

            if (string.IsNullOrEmpty(adminConnectionString))
            {
                if (!_console.IsInteractive)
                {
                    throw new InvalidOperationException($"No admin connection for server {dataSource} available, user intervention required.");
                }

                adminConnectionString = await _console.PromptAsync($"No admin connection for server {dataSource} is available, please input one: ");
            }

            var masterDbConnectionString = new SqlConnectionStringBuilder(adminConnectionString)
            {
                InitialCatalog = "master",
            };
            if (masterDbConnectionString.DataSource != dataSource)
            {
                throw new InvalidOperationException($"Admin connection is for server {masterDbConnectionString.DataSource}, but I was requested to create a connection to {dataSource}");
            }
            await using var masterDbConnection = new SqlConnection(masterDbConnectionString.ToString());

            var dbConnectionString = new SqlConnectionStringBuilder(adminConnectionString)
            {
                InitialCatalog = parameters.Database,
            };
            await using var dbConnection = new SqlConnection(dbConnectionString.ToString());

            string currentUserIndex = context.GetValue("currentUserIndex", "2");
            string nextUserId;
            int nextUserIndex;
            switch (currentUserIndex)
            {
                case "1":
                    nextUserId = context.SecretName + "-2";
                    nextUserIndex = 2;
                    break;
                case "2":
                    nextUserId = context.SecretName + "-1";
                    nextUserIndex = 1;
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected 'currentUserIndex' value '{currentUserIndex}'.");
            }

            var newPassword = GenerateRandomPassword(40);
            await masterDbConnection.OpenAsync(cancellationToken);

            var updateLoginCommand = masterDbConnection.CreateCommand();
            updateLoginCommand.CommandText = $@"
IF NOT EXISTS (
    select name
    from sys.sql_logins
    where name = '{nextUserId}')
BEGIN
    CREATE LOGIN [{nextUserId}] WITH PASSWORD = N'{newPassword}';
END
ELSE
BEGIN
    ALTER LOGIN [{nextUserId}] WITH PASSWORD = N'{newPassword}';
END";
            await updateLoginCommand.ExecuteNonQueryAsync(cancellationToken);

            if (parameters.Permissions == "admin")
            {
                var updateMasterUserCommand = masterDbConnection.CreateCommand();
                updateMasterUserCommand.CommandText = $@"
IF NOT EXISTS (
    select name
    from sys.database_principals
    where name = '{nextUserId}')
BEGIN
    CREATE USER [{nextUserId}] FOR LOGIN [{nextUserId}];
END
ALTER ROLE loginmanager ADD MEMBER [{nextUserId}];
ALTER ROLE dbmanager ADD MEMBER [{nextUserId}];
";
                await updateMasterUserCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await dbConnection.OpenAsync(cancellationToken);
            var updateUserCommand = dbConnection.CreateCommand();
            updateUserCommand.CommandText = $@"
IF NOT EXISTS (
    select name
    from sys.database_principals
    where name = '{nextUserId}')
BEGIN
    CREATE USER [{nextUserId}] FOR LOGIN [{nextUserId}];
END
";
            if (parameters.Permissions == "admin")
            {
                updateUserCommand.CommandText += $@"
ALTER ROLE db_owner ADD MEMBER [{nextUserId}]
";
            }
            else
            {
                foreach (var c in parameters.Permissions)
                {
                    switch (c)
                    {
                        case 'r':
                            updateUserCommand.CommandText += $@"
ALTER ROLE db_datareader ADD MEMBER [{nextUserId}]
";
                            break;
                        case 'w':
                            updateUserCommand.CommandText += $@"
ALTER ROLE db_datawriter ADD MEMBER [{nextUserId}]
";
                            break;
                        default:
                            throw new InvalidOperationException($"Invalid permissions specification '{c}'");
                    }
                }
            }

            await updateUserCommand.ExecuteNonQueryAsync(cancellationToken);

            context.SetValue("currentUserIndex", nextUserIndex.ToString());
            var connectionString = new SqlConnectionStringBuilder(adminConnectionString)
            {
                UserID = nextUserId,
                Password = newPassword,
                InitialCatalog = parameters.Database,
                DataSource = dataSource,
                Encrypt = true,
            };
            var result = connectionString.ToString();
            return new SecretData(result, DateTimeOffset.MaxValue, _clock.UtcNow.AddMonths(1));
        }

        private string GenerateRandomPassword(int length)
        {
            using var rng = RandomNumberGenerator.Create();
            var bytes = new byte[length];
            rng.GetNonZeroBytes(bytes);
            var result = new StringBuilder(length);
            foreach (byte b in bytes)
            {
                int value = b % 62;
                char c;
                if (value < 26)
                {
                    c = (char) ('A' + value);
                }
                else if (value < 52)
                {
                    c = (char) ('a' + value - 26);
                }
                else
                {
                    c = (char) ('0' + value - 52);
                }

                result.Append(c);
            }

            return result.ToString();
        }
    }
}
