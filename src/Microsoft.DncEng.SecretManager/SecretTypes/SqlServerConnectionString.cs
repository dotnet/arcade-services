using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.DncEng.CommandLineLib;

namespace Microsoft.DncEng.SecretManager.SecretTypes;

[Name("sql-connection-string")]
public class SqlServerConnectionString : SecretType<SqlServerConnectionString.Parameters>
{
    private readonly ISystemClock _clock;
    private readonly IConsole _console;

    public class Parameters
    {
        public SecretReference AdminConnection { get; set; }
        public string DataSource { get; set; }
        public string Database { get; set; }
        public string Permissions { get; set; }
        public string ExtraSettings { get; set; }
    }

    public SqlServerConnectionString(ISystemClock clock, IConsole console)
    {
        _clock = clock;
        _console = console;
    }

    protected override async Task<SecretData> RotateValue(Parameters parameters, RotationContext context, CancellationToken cancellationToken)
    {
        string dataSource = parameters.DataSource;

        string adminConnectionString = await context.GetSecretValue(parameters.AdminConnection);
        bool haveFullAdmin = false;

        if (string.IsNullOrEmpty(adminConnectionString))
        {
            if (!_console.IsInteractive)
            {
                throw new HumanInterventionRequiredException($"No admin connection for server {dataSource} available, user intervention required.");
            }

            adminConnectionString = await _console.PromptAsync($"No admin connection for server {dataSource} is available, please input one: ");
            haveFullAdmin = true;
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
                nextUserId = context.SecretName + "-2"; // lgtm [cs/hardcoded-credentials] Value decorates name intentionally and checked elsewhere
                nextUserIndex = 2;
                break;
            case "2":
                nextUserId = context.SecretName + "-1"; // lgtm [cs/hardcoded-credentials] Value decorates name intentionally and checked elsewhere
                nextUserIndex = 1;
                break;
            default:
                throw new InvalidOperationException($"Unexpected 'currentUserIndex' value '{currentUserIndex}'.");
        }

        var newPassword = PasswordGenerator.GenerateRandomPassword(40, false);
        await masterDbConnection.OpenAsync(cancellationToken);
        if (haveFullAdmin && parameters.Permissions == "admin")
        {
            await UpdateMasterDbWithFullAdmin(context, masterDbConnection);
        }

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
        result = OldifyConnectionString(result);
        if (!string.IsNullOrEmpty(parameters.ExtraSettings))
        {
            result += parameters.ExtraSettings;
        }
        return new SecretData(result, DateTimeOffset.MaxValue, _clock.UtcNow.AddMonths(1));
    }

    private string OldifyConnectionString(string result)
    {
        var pairs = new[]
        {
            ("ApplicationIntent", "Application Intent"),
            ("ConnectRetryCount", "Connect Retry Count"),
            ("ConnectRetryInterval", "Connect Retry Interval"),
            ("PoolBlockingPeriod", "Pool Blocking Period"),
            ("MultipleActiveResultSets", "Multiple Active Result Sets"),
            ("MultiSubnetFailover", "Multi Subnet Failover"),
            ("TransparentNetworkIPResolution", "Transparent Network IP Resolution"),
            ("TrustServerCertificate", "Trust Server Certificate"),
        };
        foreach (var (oldName, newName) in pairs)
        {
            result = result.Replace(newName, oldName);
        }

        return result;
    }

    private async Task UpdateMasterDbWithFullAdmin(RotationContext context, SqlConnection masterDbConnection)
    {
        var loginNames = new[] {context.SecretName + "-1", context.SecretName + "-2"};
        foreach (var name in loginNames)
        {
            var command = masterDbConnection.CreateCommand();
            var password = PasswordGenerator.GenerateRandomPassword(40, false);
            command.CommandText = $@"
IF NOT EXISTS (
    select name
    from sys.sql_logins
    where name = '{name}')
BEGIN
    CREATE LOGIN [{name}] WITH PASSWORD = N'{password}';
END
ELSE
BEGIN
    ALTER LOGIN [{name}] WITH PASSWORD = N'{password}';
END";;
            await command.ExecuteNonQueryAsync();

            var permissionsCommand = masterDbConnection.CreateCommand();
            permissionsCommand.CommandText = $@"
IF NOT EXISTS (
    select name
    from sys.database_principals
    where name = '{name}')
BEGIN
    CREATE USER [{name}] FOR LOGIN [{name}];
END
ALTER ROLE loginmanager ADD MEMBER [{name}];
ALTER ROLE dbmanager ADD MEMBER [{name}];
";
            await permissionsCommand.ExecuteNonQueryAsync();
        }
    }
}
