// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;

namespace Microsoft.DotNet.MaestroConfiguration.Client;

public interface IConfigurationRepositoryManager
{
    Task AddSubscriptionAsync(ConfigurationRepositoryOperationParameters parameters, SubscriptionYaml subscription);
    Task DeleteSubscriptionAsync(ConfigurationRepositoryOperationParameters parameters, SubscriptionYaml subscription);
    Task UpdateSubscriptionAsync(ConfigurationRepositoryOperationParameters parameters, SubscriptionYaml updatedSubscription);
    Task AddChannelAsync(ConfigurationRepositoryOperationParameters parameters, ChannelYaml channel);
    Task UpdateChannelAsync(ConfigurationRepositoryOperationParameters parameters, ChannelYaml updatedChannel);
    Task DeleteChannelAsync(ConfigurationRepositoryOperationParameters parameters, ChannelYaml channel);
    Task AddDefaultChannelAsync(ConfigurationRepositoryOperationParameters parameters, DefaultChannelYaml defaultChannel);
    Task DeleteDefaultChannelAsync(ConfigurationRepositoryOperationParameters parameters, DefaultChannelYaml defaultChannel);
}
