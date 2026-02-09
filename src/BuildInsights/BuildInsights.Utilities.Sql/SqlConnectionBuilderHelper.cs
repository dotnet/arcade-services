// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Data.SqlClient;

namespace BuildInsights.Utilities.Sql;

internal class SqlConnectionBuilderHelper
{
    public static string BuildConnectionString(SqlConnectionSettings _sqlSettings)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = _sqlSettings.DataSource,
            InitialCatalog = _sqlSettings.InitialCatalog,
            Authentication = _sqlSettings.Authentication,
            PersistSecurityInfo = _sqlSettings.PersistSecurityInfo,
            MultipleActiveResultSets = _sqlSettings.MultipleActiveResultSets,
            ConnectTimeout = _sqlSettings.ConnectTimeout,
            Encrypt = _sqlSettings.Encrypt,
            TrustServerCertificate = _sqlSettings.TrustServerCertificate,
            CommandTimeout = _sqlSettings.Timeout,
            MaxPoolSize = _sqlSettings.MaxPoolSize,
            UserID = _sqlSettings.UserId
        };
        return builder.ConnectionString;
    }
}
