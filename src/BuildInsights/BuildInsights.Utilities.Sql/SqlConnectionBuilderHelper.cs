// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Data.SqlClient;

namespace BuildInsights.Utilities.Sql;

public class SqlConnectionBuilderHelper
{
    public static string BuildConnectionString(SqlConnectionSettings _sqlSettings)
    {
        SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();
        builder.DataSource = _sqlSettings.DataSource;
        builder.InitialCatalog = _sqlSettings.InitialCatalog;
        builder.Authentication = _sqlSettings.Authentication;
        builder.PersistSecurityInfo = _sqlSettings.PersistSecurityInfo;
        builder.MultipleActiveResultSets = _sqlSettings.MultipleActiveResultSets;
        builder.ConnectTimeout = _sqlSettings.ConnectTimeout;
        builder.Encrypt = _sqlSettings.Encrypt;
        builder.TrustServerCertificate = _sqlSettings.TrustServerCertificate;
        builder.CommandTimeout = _sqlSettings.Timeout;
        builder.MaxPoolSize = _sqlSettings.MaxPoolSize;
        builder.UserID = _sqlSettings.UserId;
        return builder.ConnectionString;
    }
}
