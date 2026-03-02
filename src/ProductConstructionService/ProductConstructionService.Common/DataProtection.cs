// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using Azure.Core;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProductConstructionService.Common;

namespace ProductConstructionService.Common;

public static class DataProtection
{
    public const string DataProtectionKeyBlobUri = "DataProtection:KeyBlobUri";
    public const string DataProtectionKeyUri = "DataProtection:DataProtectionKeyUri";

    private static readonly TimeSpan DataProtectionKeyLifetime = new(days: 240, hours: 0, minutes: 0, seconds: 0);

    public static void AddDataProtection(this IHostApplicationBuilder builder, TokenCredential credential)
    {
        var keyBlobUri = builder.Configuration[DataProtectionKeyBlobUri];
        var dataProtectionKeyUri = builder.Configuration[DataProtectionKeyUri];

        if (string.IsNullOrEmpty(keyBlobUri) || string.IsNullOrEmpty(dataProtectionKeyUri))
        {
            builder.Services
                .AddDataProtection()
                .SetDefaultKeyLifetime(DataProtectionKeyLifetime);
            return;
        }

        builder.Services.AddDataProtection()
            .PersistKeysToAzureBlobStorage(new Uri(keyBlobUri), credential)
            .ProtectKeysWithAzureKeyVault(new Uri(dataProtectionKeyUri), credential)
            .SetDefaultKeyLifetime(DataProtectionKeyLifetime)
            .SetApplicationName(Assembly.GetEntryAssembly()?.GetName().Name!);
    }
}
