// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.Utilities.Sql;

public class SqlConnectionSettings
{
    public string DataSource { get; set; }
    public string InitialCatalog { get; set; }
    public SqlAuthenticationMethod Authentication { get; set; } = SqlAuthenticationMethod.ActiveDirectoryManagedIdentity;
    public bool PersistSecurityInfo { get; set; } = false;
    public bool MultipleActiveResultSets { get; set; } = false;
    public int ConnectTimeout { get; set; } = 30;
    public bool Encrypt { get; set; } = true;
    public bool TrustServerCertificate { get; set; } = false;
    public int Timeout { get; set; } = 300;
    public int MaxPoolSize { get; set; }
    public string UserId { get; set; }
}
