// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;

namespace ProductConstructionService.Deployment;
public class DeploymentOptions
{
    [Option("subscriptionId", Required = true, HelpText = "Azure subscription ID")]
    public required string SubscriptionId { get; init; }
    [Option("resourceGroupName", Required = true, HelpText = "Resource group name")]
    public required string ResourceGroupName { get; init; }
    [Option("pcsToken", Required = true, HelpText = "Product Construction Service token")]
    public required string PcsToken { get; set; }
    [Option("containerAppName", Required = true, HelpText = "Container app name")]
    public required string ContainerAppName { get; init; }
    [Option("newImageTag", Required = true, HelpText = "New image tag")]
    public required string NewImageTag { get; init; }
    [Option("containerRegistryName", Required = true, HelpText = "Container registry name")]
    public required string ContainerRegistryName { get; init; }
    [Option("workspaceName", Required = true, HelpText = "Workspace name")]
    public required string WorkspaceName { get; init; }
    [Option("imageName", Required = true, HelpText = "Image name")]
    public required string ImageName { get; init; }
    [Option("containerJobNames", Required = true, HelpText = "Container job names")]
    public required string ContainerJobNames { get; init; }
    [Option("azCliPath", Required = true, HelpText = "Path to Azure CLI")]
    public required string AzCliPath { get; init; }
}
