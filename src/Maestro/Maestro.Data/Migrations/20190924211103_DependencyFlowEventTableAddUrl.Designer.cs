﻿// <auto-generated />
using System;
using Maestro.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Maestro.Data.Migrations
{
    [DbContext(typeof(BuildAssetRegistryContext))]
    [Migration("20190924211103_DependencyFlowEventTableAddUrl")]
    partial class DependencyFlowEventTableAddUrl
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "2.1.1-rtm-30846")
                .HasAnnotation("Relational:MaxIdentifierLength", 128)
                .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

            modelBuilder.Entity("Maestro.Data.ApplicationUser", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

                    b.Property<int>("AccessFailedCount");

                    b.Property<string>("ConcurrencyStamp")
                        .IsConcurrencyToken();

                    b.Property<string>("Email")
                        .HasMaxLength(256);

                    b.Property<bool>("EmailConfirmed");

                    b.Property<string>("FullName");

                    b.Property<DateTimeOffset>("LastUpdated");

                    b.Property<bool>("LockoutEnabled");

                    b.Property<DateTimeOffset?>("LockoutEnd");

                    b.Property<string>("NormalizedEmail")
                        .HasMaxLength(256);

                    b.Property<string>("NormalizedUserName")
                        .HasMaxLength(256);

                    b.Property<string>("PasswordHash");

                    b.Property<string>("PhoneNumber");

                    b.Property<bool>("PhoneNumberConfirmed");

                    b.Property<string>("SecurityStamp");

                    b.Property<bool>("TwoFactorEnabled");

                    b.Property<string>("UserName")
                        .HasMaxLength(256);

                    b.HasKey("Id");

                    b.HasIndex("NormalizedEmail")
                        .HasName("EmailIndex");

                    b.HasIndex("NormalizedUserName")
                        .IsUnique()
                        .HasName("UserNameIndex")
                        .HasFilter("[NormalizedUserName] IS NOT NULL");

                    b.ToTable("AspNetUsers");
                });

            modelBuilder.Entity("Maestro.Data.ApplicationUserPersonalAccessToken", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

                    b.Property<int>("ApplicationUserId");

                    b.Property<DateTimeOffset>("Created");

                    b.Property<string>("Hash");

                    b.Property<string>("Name");

                    b.HasKey("Id");

                    b.HasIndex("ApplicationUserId", "Name")
                        .IsUnique()
                        .HasFilter("[Name] IS NOT NULL");

                    b.ToTable("AspNetUserPersonalAccessTokens");
                });

            modelBuilder.Entity("Maestro.Data.Models.Asset", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

                    b.Property<int>("BuildId");

                    b.Property<string>("Name")
                        .HasMaxLength(150);

                    b.Property<bool>("NonShipping");

                    b.Property<string>("Version")
                        .HasMaxLength(75);

                    b.HasKey("Id");

                    b.HasIndex("BuildId");

                    b.HasIndex("Name", "Version");

                    b.ToTable("Assets");
                });

            modelBuilder.Entity("Maestro.Data.Models.AssetLocation", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

                    b.Property<int?>("AssetId");

                    b.Property<string>("Location");

                    b.Property<int>("Type");

                    b.HasKey("Id");

                    b.HasIndex("AssetId");

                    b.ToTable("AssetLocations");
                });

            modelBuilder.Entity("Maestro.Data.Models.Build", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

                    b.Property<string>("AzureDevOpsAccount");

                    b.Property<string>("AzureDevOpsBranch");

                    b.Property<int?>("AzureDevOpsBuildDefinitionId");

                    b.Property<int?>("AzureDevOpsBuildId");

                    b.Property<string>("AzureDevOpsBuildNumber");

                    b.Property<string>("AzureDevOpsProject");

                    b.Property<string>("AzureDevOpsRepository");

                    b.Property<string>("Commit");

                    b.Property<DateTimeOffset>("DateProduced");

                    b.Property<string>("GitHubBranch");

                    b.Property<string>("GitHubRepository");

                    b.Property<bool>("PublishUsingPipelines");

                    b.HasKey("Id");

                    b.ToTable("Builds");
                });

            modelBuilder.Entity("Maestro.Data.Models.BuildChannel", b =>
                {
                    b.Property<int>("BuildId");

                    b.Property<int>("ChannelId");

                    b.HasKey("BuildId", "ChannelId");

                    b.HasIndex("ChannelId");

                    b.ToTable("BuildChannels");
                });

            modelBuilder.Entity("Maestro.Data.Models.BuildDependency", b =>
                {
                    b.Property<int>("BuildId");

                    b.Property<int>("DependentBuildId");

                    b.Property<bool>("IsProduct");

                    b.HasKey("BuildId", "DependentBuildId");

                    b.HasIndex("DependentBuildId");

                    b.ToTable("BuildDependencies");
                });

            modelBuilder.Entity("Maestro.Data.Models.Channel", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

                    b.Property<string>("Classification")
                        .IsRequired();

                    b.Property<string>("Name")
                        .IsRequired();

                    b.HasKey("Id");

                    b.HasIndex("Name")
                        .IsUnique();

                    b.ToTable("Channels");
                });

            modelBuilder.Entity("Maestro.Data.Models.ChannelReleasePipeline", b =>
                {
                    b.Property<int>("ChannelId");

                    b.Property<int>("ReleasePipelineId");

                    b.HasKey("ChannelId", "ReleasePipelineId");

                    b.HasIndex("ReleasePipelineId");

                    b.ToTable("ChannelReleasePipelines");
                });

            modelBuilder.Entity("Maestro.Data.Models.DefaultChannel", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

                    b.Property<string>("Branch")
                        .IsRequired()
                        .HasColumnType("varchar(100)")
                        .HasMaxLength(100);

                    b.Property<int>("ChannelId");

                    b.Property<bool>("Enabled");

                    b.Property<string>("Repository")
                        .IsRequired()
                        .HasColumnType("varchar(300)")
                        .HasMaxLength(300);

                    b.HasKey("Id");

                    b.HasIndex("ChannelId");

                    b.HasIndex("Repository", "Branch", "ChannelId")
                        .IsUnique();

                    b.ToTable("DefaultChannels");
                });

            modelBuilder.Entity("Maestro.Data.Models.DependencyFlowEvent", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

                    b.Property<int>("BuildId");

                    b.Property<int?>("ChannelId");

                    b.Property<string>("Event");

                    b.Property<string>("FlowType");

                    b.Property<string>("Reason");

                    b.Property<string>("SourceRepository")
                        .HasMaxLength(450);

                    b.Property<string>("TargetRepository")
                        .HasMaxLength(450);

                    b.Property<DateTimeOffset>("Timestamp");

                    b.Property<string>("Url");

                    b.HasKey("Id");

                    b.HasIndex("BuildId");

                    b.ToTable("DependencyFlowEvents");
                });

            modelBuilder.Entity("Maestro.Data.Models.ReleasePipeline", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

                    b.Property<string>("Organization");

                    b.Property<int>("PipelineIdentifier");

                    b.Property<string>("Project");

                    b.HasKey("Id");

                    b.ToTable("ReleasePipelines");
                });

            modelBuilder.Entity("Maestro.Data.Models.Repository", b =>
                {
                    b.Property<string>("RepositoryName")
                        .ValueGeneratedOnAdd()
                        .HasMaxLength(450);

                    b.Property<long>("InstallationId");

                    b.HasKey("RepositoryName");

                    b.ToTable("Repositories");
                });

            modelBuilder.Entity("Maestro.Data.Models.RepositoryBranch", b =>
                {
                    b.Property<string>("RepositoryName")
                        .HasMaxLength(450);

                    b.Property<string>("BranchName")
                        .HasMaxLength(450);

                    b.Property<string>("PolicyString")
                        .HasColumnName("Policy");

                    b.HasKey("RepositoryName", "BranchName");

                    b.ToTable("RepositoryBranches");
                });

            modelBuilder.Entity("Maestro.Data.Models.RepositoryBranchUpdate", b =>
                {
                    b.Property<string>("RepositoryName")
                        .HasMaxLength(450);

                    b.Property<string>("BranchName")
                        .HasMaxLength(450);

                    b.Property<string>("Action");

                    b.Property<string>("Arguments");

                    b.Property<string>("ErrorMessage");

                    b.Property<string>("Method");

                    b.Property<bool>("Success");

                    b.Property<DateTime>("SysEndTime")
                        .ValueGeneratedOnAddOrUpdate()
                        .HasColumnType("datetime2 GENERATED ALWAYS AS ROW END");

                    b.Property<DateTime>("SysStartTime")
                        .ValueGeneratedOnAddOrUpdate()
                        .HasColumnType("datetime2 GENERATED ALWAYS AS ROW START");

                    b.HasKey("RepositoryName", "BranchName");

                    b.ToTable("RepositoryBranchUpdates");

                    b.HasAnnotation("SqlServer:HistoryRetentionPeriod", "6 MONTH");

                    b.HasAnnotation("SqlServer:SystemVersioned", "Maestro.Data.Models.RepositoryBranchUpdateHistory");
                });

            modelBuilder.Entity("Maestro.Data.Models.RepositoryBranchUpdateHistory", b =>
                {
                    b.Property<string>("RepositoryName")
                        .HasMaxLength(450);

                    b.Property<string>("BranchName")
                        .HasMaxLength(450);

                    b.Property<string>("Action");

                    b.Property<string>("Arguments");

                    b.Property<string>("ErrorMessage");

                    b.Property<string>("Method");

                    b.Property<bool>("Success");

                    b.Property<DateTime>("SysEndTime")
                        .ValueGeneratedOnAddOrUpdate()
                        .HasColumnType("datetime2");

                    b.Property<DateTime>("SysStartTime")
                        .ValueGeneratedOnAddOrUpdate()
                        .HasColumnType("datetime2");

                    b.HasKey("RepositoryName", "BranchName");

                    b.HasIndex("SysEndTime", "SysStartTime")
                        .HasAnnotation("SqlServer:Clustered", true);

                    b.HasIndex("RepositoryName", "BranchName", "SysEndTime", "SysStartTime");

                    b.ToTable("RepositoryBranchUpdateHistory");

                    b.HasAnnotation("SqlServer:HistoryTable", true);
                });

            modelBuilder.Entity("Maestro.Data.Models.Subscription", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<int>("ChannelId");

                    b.Property<bool>("Enabled");

                    b.Property<int?>("LastAppliedBuildId");

                    b.Property<string>("PolicyString")
                        .HasColumnName("Policy");

                    b.Property<string>("SourceRepository");

                    b.Property<string>("TargetBranch");

                    b.Property<string>("TargetRepository");

                    b.HasKey("Id");

                    b.HasIndex("ChannelId");

                    b.HasIndex("LastAppliedBuildId");

                    b.ToTable("Subscriptions");
                });

            modelBuilder.Entity("Maestro.Data.Models.SubscriptionUpdate", b =>
                {
                    b.Property<Guid>("SubscriptionId");

                    b.Property<string>("Action");

                    b.Property<string>("Arguments");

                    b.Property<string>("ErrorMessage");

                    b.Property<string>("Method");

                    b.Property<bool>("Success");

                    b.Property<DateTime>("SysEndTime")
                        .ValueGeneratedOnAddOrUpdate()
                        .HasColumnType("datetime2 GENERATED ALWAYS AS ROW END");

                    b.Property<DateTime>("SysStartTime")
                        .ValueGeneratedOnAddOrUpdate()
                        .HasColumnType("datetime2 GENERATED ALWAYS AS ROW START");

                    b.HasKey("SubscriptionId");

                    b.ToTable("SubscriptionUpdates");

                    b.HasAnnotation("SqlServer:HistoryRetentionPeriod", "6 MONTH");

                    b.HasAnnotation("SqlServer:SystemVersioned", "Maestro.Data.Models.SubscriptionUpdateHistory");
                });

            modelBuilder.Entity("Maestro.Data.Models.SubscriptionUpdateHistory", b =>
                {
                    b.Property<Guid>("SubscriptionId")
                        .ValueGeneratedOnAdd();

                    b.Property<string>("Action");

                    b.Property<string>("Arguments");

                    b.Property<string>("ErrorMessage");

                    b.Property<string>("Method");

                    b.Property<bool>("Success");

                    b.Property<DateTime>("SysEndTime")
                        .ValueGeneratedOnAddOrUpdate()
                        .HasColumnType("datetime2");

                    b.Property<DateTime>("SysStartTime")
                        .ValueGeneratedOnAddOrUpdate()
                        .HasColumnType("datetime2");

                    b.HasKey("SubscriptionId");

                    b.HasIndex("SysEndTime", "SysStartTime")
                        .HasAnnotation("SqlServer:Clustered", true);

                    b.HasIndex("SubscriptionId", "SysEndTime", "SysStartTime");

                    b.ToTable("SubscriptionUpdateHistory");

                    b.HasAnnotation("SqlServer:HistoryTable", true);
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityRole<int>", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

                    b.Property<string>("ConcurrencyStamp")
                        .IsConcurrencyToken();

                    b.Property<string>("Name")
                        .HasMaxLength(256);

                    b.Property<string>("NormalizedName")
                        .HasMaxLength(256);

                    b.HasKey("Id");

                    b.HasIndex("NormalizedName")
                        .IsUnique()
                        .HasName("RoleNameIndex")
                        .HasFilter("[NormalizedName] IS NOT NULL");

                    b.ToTable("AspNetRoles");
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityRoleClaim<int>", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

                    b.Property<string>("ClaimType");

                    b.Property<string>("ClaimValue");

                    b.Property<int>("RoleId");

                    b.HasKey("Id");

                    b.HasIndex("RoleId");

                    b.ToTable("AspNetRoleClaims");
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserClaim<int>", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

                    b.Property<string>("ClaimType");

                    b.Property<string>("ClaimValue");

                    b.Property<int>("UserId");

                    b.HasKey("Id");

                    b.HasIndex("UserId");

                    b.ToTable("AspNetUserClaims");
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserLogin<int>", b =>
                {
                    b.Property<string>("LoginProvider");

                    b.Property<string>("ProviderKey");

                    b.Property<string>("ProviderDisplayName");

                    b.Property<int>("UserId");

                    b.HasKey("LoginProvider", "ProviderKey");

                    b.HasIndex("UserId");

                    b.ToTable("AspNetUserLogins");
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserRole<int>", b =>
                {
                    b.Property<int>("UserId");

                    b.Property<int>("RoleId");

                    b.HasKey("UserId", "RoleId");

                    b.HasIndex("RoleId");

                    b.ToTable("AspNetUserRoles");
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserToken<int>", b =>
                {
                    b.Property<int>("UserId");

                    b.Property<string>("LoginProvider");

                    b.Property<string>("Name");

                    b.Property<string>("Value");

                    b.HasKey("UserId", "LoginProvider", "Name");

                    b.ToTable("AspNetUserTokens");
                });

            modelBuilder.Entity("Maestro.Data.ApplicationUserPersonalAccessToken", b =>
                {
                    b.HasOne("Maestro.Data.ApplicationUser", "ApplicationUser")
                        .WithMany("PersonalAccessTokens")
                        .HasForeignKey("ApplicationUserId")
                        .OnDelete(DeleteBehavior.Cascade);
                });

            modelBuilder.Entity("Maestro.Data.Models.Asset", b =>
                {
                    b.HasOne("Maestro.Data.Models.Build")
                        .WithMany("Assets")
                        .HasForeignKey("BuildId")
                        .OnDelete(DeleteBehavior.Cascade);
                });

            modelBuilder.Entity("Maestro.Data.Models.AssetLocation", b =>
                {
                    b.HasOne("Maestro.Data.Models.Asset")
                        .WithMany("Locations")
                        .HasForeignKey("AssetId");
                });

            modelBuilder.Entity("Maestro.Data.Models.BuildChannel", b =>
                {
                    b.HasOne("Maestro.Data.Models.Build", "Build")
                        .WithMany("BuildChannels")
                        .HasForeignKey("BuildId")
                        .OnDelete(DeleteBehavior.Cascade);

                    b.HasOne("Maestro.Data.Models.Channel", "Channel")
                        .WithMany("BuildChannels")
                        .HasForeignKey("ChannelId")
                        .OnDelete(DeleteBehavior.Cascade);
                });

            modelBuilder.Entity("Maestro.Data.Models.BuildDependency", b =>
                {
                    b.HasOne("Maestro.Data.Models.Build", "Build")
                        .WithMany()
                        .HasForeignKey("BuildId")
                        .OnDelete(DeleteBehavior.Restrict);

                    b.HasOne("Maestro.Data.Models.Build", "DependentBuild")
                        .WithMany()
                        .HasForeignKey("DependentBuildId")
                        .OnDelete(DeleteBehavior.Restrict);
                });

            modelBuilder.Entity("Maestro.Data.Models.ChannelReleasePipeline", b =>
                {
                    b.HasOne("Maestro.Data.Models.Channel", "Channel")
                        .WithMany("ChannelReleasePipelines")
                        .HasForeignKey("ChannelId")
                        .OnDelete(DeleteBehavior.Restrict);

                    b.HasOne("Maestro.Data.Models.ReleasePipeline", "ReleasePipeline")
                        .WithMany("ChannelReleasePipelines")
                        .HasForeignKey("ReleasePipelineId")
                        .OnDelete(DeleteBehavior.Restrict);
                });

            modelBuilder.Entity("Maestro.Data.Models.DefaultChannel", b =>
                {
                    b.HasOne("Maestro.Data.Models.Channel", "Channel")
                        .WithMany("DefaultChannels")
                        .HasForeignKey("ChannelId")
                        .OnDelete(DeleteBehavior.Cascade);
                });

            modelBuilder.Entity("Maestro.Data.Models.DependencyFlowEvent", b =>
                {
                    b.HasOne("Maestro.Data.Models.Build", "Build")
                        .WithMany()
                        .HasForeignKey("BuildId")
                        .OnDelete(DeleteBehavior.Cascade);
                });

            modelBuilder.Entity("Maestro.Data.Models.RepositoryBranch", b =>
                {
                    b.HasOne("Maestro.Data.Models.Repository", "Repository")
                        .WithMany("Branches")
                        .HasForeignKey("RepositoryName")
                        .OnDelete(DeleteBehavior.Cascade);
                });

            modelBuilder.Entity("Maestro.Data.Models.RepositoryBranchUpdate", b =>
                {
                    b.HasOne("Maestro.Data.Models.RepositoryBranch", "RepositoryBranch")
                        .WithOne()
                        .HasForeignKey("Maestro.Data.Models.RepositoryBranchUpdate", "RepositoryName", "BranchName")
                        .OnDelete(DeleteBehavior.Restrict);
                });

            modelBuilder.Entity("Maestro.Data.Models.Subscription", b =>
                {
                    b.HasOne("Maestro.Data.Models.Channel", "Channel")
                        .WithMany()
                        .HasForeignKey("ChannelId")
                        .OnDelete(DeleteBehavior.Cascade);

                    b.HasOne("Maestro.Data.Models.Build", "LastAppliedBuild")
                        .WithMany()
                        .HasForeignKey("LastAppliedBuildId");
                });

            modelBuilder.Entity("Maestro.Data.Models.SubscriptionUpdate", b =>
                {
                    b.HasOne("Maestro.Data.Models.Subscription", "Subscription")
                        .WithOne()
                        .HasForeignKey("Maestro.Data.Models.SubscriptionUpdate", "SubscriptionId")
                        .OnDelete(DeleteBehavior.Restrict);
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityRoleClaim<int>", b =>
                {
                    b.HasOne("Microsoft.AspNetCore.Identity.IdentityRole<int>")
                        .WithMany()
                        .HasForeignKey("RoleId")
                        .OnDelete(DeleteBehavior.Cascade);
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserClaim<int>", b =>
                {
                    b.HasOne("Maestro.Data.ApplicationUser")
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade);
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserLogin<int>", b =>
                {
                    b.HasOne("Maestro.Data.ApplicationUser")
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade);
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserRole<int>", b =>
                {
                    b.HasOne("Microsoft.AspNetCore.Identity.IdentityRole<int>")
                        .WithMany()
                        .HasForeignKey("RoleId")
                        .OnDelete(DeleteBehavior.Cascade);

                    b.HasOne("Maestro.Data.ApplicationUser")
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade);
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserToken<int>", b =>
                {
                    b.HasOne("Maestro.Data.ApplicationUser")
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade);
                });
#pragma warning restore 612, 618
        }
    }
}
