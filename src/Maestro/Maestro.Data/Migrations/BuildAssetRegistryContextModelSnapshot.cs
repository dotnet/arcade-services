﻿// <auto-generated />
using System;
using Maestro.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Maestro.Data.Migrations
{
    [DbContext(typeof(BuildAssetRegistryContext))]
    partial class BuildAssetRegistryContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "3.1.3")
                .HasAnnotation("Relational:MaxIdentifierLength", 128)
                .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

            modelBuilder.Entity("Maestro.Data.ApplicationUser", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("int")
                        .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

                    b.Property<int>("AccessFailedCount")
                        .HasColumnType("int");

                    b.Property<string>("ConcurrencyStamp")
                        .IsConcurrencyToken()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("Email")
                        .HasColumnType("nvarchar(256)")
                        .HasMaxLength(256);

                    b.Property<bool>("EmailConfirmed")
                        .HasColumnType("bit");

                    b.Property<string>("FullName")
                        .HasColumnType("nvarchar(max)");

                    b.Property<DateTimeOffset>("LastUpdated")
                        .HasColumnType("datetimeoffset");

                    b.Property<bool>("LockoutEnabled")
                        .HasColumnType("bit");

                    b.Property<DateTimeOffset?>("LockoutEnd")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("NormalizedEmail")
                        .HasColumnType("nvarchar(256)")
                        .HasMaxLength(256);

                    b.Property<string>("NormalizedUserName")
                        .HasColumnType("nvarchar(256)")
                        .HasMaxLength(256);

                    b.Property<string>("PasswordHash")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("PhoneNumber")
                        .HasColumnType("nvarchar(max)");

                    b.Property<bool>("PhoneNumberConfirmed")
                        .HasColumnType("bit");

                    b.Property<string>("SecurityStamp")
                        .HasColumnType("nvarchar(max)");

                    b.Property<bool>("TwoFactorEnabled")
                        .HasColumnType("bit");

                    b.Property<string>("UserName")
                        .HasColumnType("nvarchar(256)")
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
                        .HasColumnType("int")
                        .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

                    b.Property<int>("ApplicationUserId")
                        .HasColumnType("int");

                    b.Property<DateTimeOffset>("Created")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("Hash")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("Name")
                        .HasColumnType("nvarchar(450)");

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
                        .HasColumnType("int")
                        .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

                    b.Property<int>("BuildId")
                        .HasColumnType("int");

                    b.Property<string>("Name")
                        .HasColumnType("nvarchar(150)")
                        .HasMaxLength(150);

                    b.Property<bool>("NonShipping")
                        .HasColumnType("bit");

                    b.Property<string>("Version")
                        .HasColumnType("nvarchar(75)")
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
                        .HasColumnType("int")
                        .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

                    b.Property<int?>("AssetId")
                        .HasColumnType("int");

                    b.Property<string>("Location")
                        .HasColumnType("nvarchar(max)");

                    b.Property<int>("Type")
                        .HasColumnType("int");

                    b.HasKey("Id");

                    b.HasIndex("AssetId");

                    b.ToTable("AssetLocations");
                });

            modelBuilder.Entity("Maestro.Data.Models.Build", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("int")
                        .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

                    b.Property<string>("AzureDevOpsAccount")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("AzureDevOpsBranch")
                        .HasColumnType("nvarchar(max)");

                    b.Property<int?>("AzureDevOpsBuildDefinitionId")
                        .HasColumnType("int");

                    b.Property<int?>("AzureDevOpsBuildId")
                        .HasColumnType("int");

                    b.Property<string>("AzureDevOpsBuildNumber")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("AzureDevOpsProject")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("AzureDevOpsRepository")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("Commit")
                        .HasColumnType("nvarchar(max)");

                    b.Property<DateTimeOffset>("DateProduced")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("GitHubBranch")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("GitHubRepository")
                        .HasColumnType("nvarchar(max)");

                    b.Property<bool>("Released")
                        .HasColumnType("bit");

                    b.Property<bool>("Stable")
                        .HasColumnType("bit");

                    b.HasKey("Id");

                    b.ToTable("Builds");
                });

            modelBuilder.Entity("Maestro.Data.Models.BuildChannel", b =>
                {
                    b.Property<int>("BuildId")
                        .HasColumnType("int");

                    b.Property<int>("ChannelId")
                        .HasColumnType("int");

                    b.Property<DateTimeOffset>("DateTimeAdded")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("BuildId", "ChannelId");

                    b.HasIndex("ChannelId");

                    b.ToTable("BuildChannels");
                });

            modelBuilder.Entity("Maestro.Data.Models.BuildDependency", b =>
                {
                    b.Property<int>("BuildId")
                        .HasColumnType("int");

                    b.Property<int>("DependentBuildId")
                        .HasColumnType("int");

                    b.Property<bool>("IsProduct")
                        .HasColumnType("bit");

                    b.Property<double>("TimeToInclusionInMinutes")
                        .HasColumnType("float");

                    b.HasKey("BuildId", "DependentBuildId");

                    b.HasIndex("DependentBuildId");

                    b.ToTable("BuildDependencies");
                });

            modelBuilder.Entity("Maestro.Data.Models.BuildIncoherence", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("int")
                        .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

                    b.Property<int?>("BuildId")
                        .HasColumnType("int");

                    b.Property<string>("Commit")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("Name")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("Repository")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("Version")
                        .HasColumnType("nvarchar(max)");

                    b.HasKey("Id");

                    b.HasIndex("BuildId");

                    b.ToTable("BuildIncoherencies");
                });

            modelBuilder.Entity("Maestro.Data.Models.Channel", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("int")
                        .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

                    b.Property<string>("Classification")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("nvarchar(450)");

                    b.HasKey("Id");

                    b.HasIndex("Name")
                        .IsUnique();

                    b.ToTable("Channels");
                });

            modelBuilder.Entity("Maestro.Data.Models.DefaultChannel", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("int")
                        .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

                    b.Property<string>("Branch")
                        .IsRequired()
                        .HasColumnType("varchar(100)")
                        .HasMaxLength(100);

                    b.Property<int>("ChannelId")
                        .HasColumnType("int");

                    b.Property<bool>("Enabled")
                        .HasColumnType("bit");

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
                        .HasColumnType("bigint")
                        .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

                    b.Property<int>("BuildId")
                        .HasColumnType("int");

                    b.Property<int?>("ChannelId")
                        .HasColumnType("int");

                    b.Property<string>("Event")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("FlowType")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("Reason")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("SourceRepository")
                        .HasColumnType("nvarchar(450)")
                        .HasMaxLength(450);

                    b.Property<string>("TargetRepository")
                        .HasColumnType("nvarchar(450)")
                        .HasMaxLength(450);

                    b.Property<DateTimeOffset>("Timestamp")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("Url")
                        .HasColumnType("nvarchar(max)");

                    b.HasKey("Id");

                    b.HasIndex("BuildId");

                    b.ToTable("DependencyFlowEvents");
                });

            modelBuilder.Entity("Maestro.Data.Models.GoalTime", b =>
                {
                    b.Property<int>("DefinitionId")
                        .HasColumnType("int");

                    b.Property<int>("ChannelId")
                        .HasColumnType("int");

                    b.Property<int>("Minutes")
                        .HasColumnType("int");

                    b.HasKey("DefinitionId", "ChannelId");

                    b.HasIndex("ChannelId");

                    b.ToTable("GoalTime");
                });

            modelBuilder.Entity("Maestro.Data.Models.LongestBuildPath", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("int")
                        .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

                    b.Property<double>("BestCaseTimeInMinutes")
                        .HasColumnType("float");

                    b.Property<int>("ChannelId")
                        .HasColumnType("int");

                    b.Property<string>("ContributingRepositories")
                        .HasColumnType("nvarchar(max)");

                    b.Property<DateTimeOffset>("ReportDate")
                        .HasColumnType("datetimeoffset");

                    b.Property<double>("WorstCaseTimeInMinutes")
                        .HasColumnType("float");

                    b.HasKey("Id");

                    b.HasIndex("ChannelId");

                    b.ToTable("LongestBuildPaths");
                });

            modelBuilder.Entity("Maestro.Data.Models.Repository", b =>
                {
                    b.Property<string>("RepositoryName")
                        .HasColumnType("nvarchar(450)")
                        .HasMaxLength(450);

                    b.Property<long>("InstallationId")
                        .HasColumnType("bigint");

                    b.HasKey("RepositoryName");

                    b.ToTable("Repositories");
                });

            modelBuilder.Entity("Maestro.Data.Models.RepositoryBranch", b =>
                {
                    b.Property<string>("RepositoryName")
                        .HasColumnType("nvarchar(450)")
                        .HasMaxLength(450);

                    b.Property<string>("BranchName")
                        .HasColumnType("nvarchar(450)")
                        .HasMaxLength(450);

                    b.Property<string>("PolicyString")
                        .HasColumnName("Policy")
                        .HasColumnType("nvarchar(max)");

                    b.HasKey("RepositoryName", "BranchName");

                    b.ToTable("RepositoryBranches");
                });

            modelBuilder.Entity("Maestro.Data.Models.RepositoryBranchUpdate", b =>
                {
                    b.Property<string>("RepositoryName")
                        .HasColumnType("nvarchar(450)")
                        .HasMaxLength(450);

                    b.Property<string>("BranchName")
                        .HasColumnType("nvarchar(450)")
                        .HasMaxLength(450);

                    b.Property<string>("Action")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("Arguments")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("ErrorMessage")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("Method")
                        .HasColumnType("nvarchar(max)");

                    b.Property<bool>("Success")
                        .HasColumnType("bit");

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
                        .HasColumnType("nvarchar(450)")
                        .HasMaxLength(450);

                    b.Property<string>("BranchName")
                        .HasColumnType("nvarchar(450)")
                        .HasMaxLength(450);

                    b.Property<string>("Action")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("Arguments")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("ErrorMessage")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("Method")
                        .HasColumnType("nvarchar(max)");

                    b.Property<bool>("Success")
                        .HasColumnType("bit");

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
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<int>("ChannelId")
                        .HasColumnType("int");

                    b.Property<bool>("Enabled")
                        .HasColumnType("bit");

                    b.Property<int?>("LastAppliedBuildId")
                        .HasColumnType("int");

                    b.Property<string>("PolicyString")
                        .HasColumnName("Policy")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("SourceRepository")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("TargetBranch")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("TargetRepository")
                        .HasColumnType("nvarchar(max)");

                    b.HasKey("Id");

                    b.HasIndex("ChannelId");

                    b.HasIndex("LastAppliedBuildId");

                    b.ToTable("Subscriptions");
                });

            modelBuilder.Entity("Maestro.Data.Models.SubscriptionUpdate", b =>
                {
                    b.Property<Guid>("SubscriptionId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("Action")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("Arguments")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("ErrorMessage")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("Method")
                        .HasColumnType("nvarchar(max)");

                    b.Property<bool>("Success")
                        .HasColumnType("bit");

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
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("Action")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("Arguments")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("ErrorMessage")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("Method")
                        .HasColumnType("nvarchar(max)");

                    b.Property<bool>("Success")
                        .HasColumnType("bit");

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
                        .HasColumnType("int")
                        .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

                    b.Property<string>("ConcurrencyStamp")
                        .IsConcurrencyToken()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("Name")
                        .HasColumnType("nvarchar(256)")
                        .HasMaxLength(256);

                    b.Property<string>("NormalizedName")
                        .HasColumnType("nvarchar(256)")
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
                        .HasColumnType("int")
                        .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

                    b.Property<string>("ClaimType")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("ClaimValue")
                        .HasColumnType("nvarchar(max)");

                    b.Property<int>("RoleId")
                        .HasColumnType("int");

                    b.HasKey("Id");

                    b.HasIndex("RoleId");

                    b.ToTable("AspNetRoleClaims");
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserClaim<int>", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("int")
                        .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

                    b.Property<string>("ClaimType")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("ClaimValue")
                        .HasColumnType("nvarchar(max)");

                    b.Property<int>("UserId")
                        .HasColumnType("int");

                    b.HasKey("Id");

                    b.HasIndex("UserId");

                    b.ToTable("AspNetUserClaims");
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserLogin<int>", b =>
                {
                    b.Property<string>("LoginProvider")
                        .HasColumnType("nvarchar(450)");

                    b.Property<string>("ProviderKey")
                        .HasColumnType("nvarchar(450)");

                    b.Property<string>("ProviderDisplayName")
                        .HasColumnType("nvarchar(max)");

                    b.Property<int>("UserId")
                        .HasColumnType("int");

                    b.HasKey("LoginProvider", "ProviderKey");

                    b.HasIndex("UserId");

                    b.ToTable("AspNetUserLogins");
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserRole<int>", b =>
                {
                    b.Property<int>("UserId")
                        .HasColumnType("int");

                    b.Property<int>("RoleId")
                        .HasColumnType("int");

                    b.HasKey("UserId", "RoleId");

                    b.HasIndex("RoleId");

                    b.ToTable("AspNetUserRoles");
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserToken<int>", b =>
                {
                    b.Property<int>("UserId")
                        .HasColumnType("int");

                    b.Property<string>("LoginProvider")
                        .HasColumnType("nvarchar(450)");

                    b.Property<string>("Name")
                        .HasColumnType("nvarchar(450)");

                    b.Property<string>("Value")
                        .HasColumnType("nvarchar(max)");

                    b.HasKey("UserId", "LoginProvider", "Name");

                    b.ToTable("AspNetUserTokens");
                });

            modelBuilder.Entity("Maestro.Data.ApplicationUserPersonalAccessToken", b =>
                {
                    b.HasOne("Maestro.Data.ApplicationUser", "ApplicationUser")
                        .WithMany("PersonalAccessTokens")
                        .HasForeignKey("ApplicationUserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("Maestro.Data.Models.Asset", b =>
                {
                    b.HasOne("Maestro.Data.Models.Build", null)
                        .WithMany("Assets")
                        .HasForeignKey("BuildId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("Maestro.Data.Models.AssetLocation", b =>
                {
                    b.HasOne("Maestro.Data.Models.Asset", null)
                        .WithMany("Locations")
                        .HasForeignKey("AssetId");
                });

            modelBuilder.Entity("Maestro.Data.Models.BuildChannel", b =>
                {
                    b.HasOne("Maestro.Data.Models.Build", "Build")
                        .WithMany("BuildChannels")
                        .HasForeignKey("BuildId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("Maestro.Data.Models.Channel", "Channel")
                        .WithMany("BuildChannels")
                        .HasForeignKey("ChannelId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("Maestro.Data.Models.BuildDependency", b =>
                {
                    b.HasOne("Maestro.Data.Models.Build", "Build")
                        .WithMany()
                        .HasForeignKey("BuildId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.HasOne("Maestro.Data.Models.Build", "DependentBuild")
                        .WithMany()
                        .HasForeignKey("DependentBuildId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();
                });

            modelBuilder.Entity("Maestro.Data.Models.BuildIncoherence", b =>
                {
                    b.HasOne("Maestro.Data.Models.Build", null)
                        .WithMany("Incoherencies")
                        .HasForeignKey("BuildId");
                });

            modelBuilder.Entity("Maestro.Data.Models.DefaultChannel", b =>
                {
                    b.HasOne("Maestro.Data.Models.Channel", "Channel")
                        .WithMany("DefaultChannels")
                        .HasForeignKey("ChannelId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("Maestro.Data.Models.DependencyFlowEvent", b =>
                {
                    b.HasOne("Maestro.Data.Models.Build", "Build")
                        .WithMany()
                        .HasForeignKey("BuildId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("Maestro.Data.Models.GoalTime", b =>
                {
                    b.HasOne("Maestro.Data.Models.Channel", "Channel")
                        .WithMany()
                        .HasForeignKey("ChannelId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("Maestro.Data.Models.LongestBuildPath", b =>
                {
                    b.HasOne("Maestro.Data.Models.Channel", "Channel")
                        .WithMany()
                        .HasForeignKey("ChannelId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("Maestro.Data.Models.RepositoryBranch", b =>
                {
                    b.HasOne("Maestro.Data.Models.Repository", "Repository")
                        .WithMany("Branches")
                        .HasForeignKey("RepositoryName")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("Maestro.Data.Models.RepositoryBranchUpdate", b =>
                {
                    b.HasOne("Maestro.Data.Models.RepositoryBranch", "RepositoryBranch")
                        .WithOne()
                        .HasForeignKey("Maestro.Data.Models.RepositoryBranchUpdate", "RepositoryName", "BranchName")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();
                });

            modelBuilder.Entity("Maestro.Data.Models.Subscription", b =>
                {
                    b.HasOne("Maestro.Data.Models.Channel", "Channel")
                        .WithMany()
                        .HasForeignKey("ChannelId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("Maestro.Data.Models.Build", "LastAppliedBuild")
                        .WithMany()
                        .HasForeignKey("LastAppliedBuildId");
                });

            modelBuilder.Entity("Maestro.Data.Models.SubscriptionUpdate", b =>
                {
                    b.HasOne("Maestro.Data.Models.Subscription", "Subscription")
                        .WithOne()
                        .HasForeignKey("Maestro.Data.Models.SubscriptionUpdate", "SubscriptionId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityRoleClaim<int>", b =>
                {
                    b.HasOne("Microsoft.AspNetCore.Identity.IdentityRole<int>", null)
                        .WithMany()
                        .HasForeignKey("RoleId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserClaim<int>", b =>
                {
                    b.HasOne("Maestro.Data.ApplicationUser", null)
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserLogin<int>", b =>
                {
                    b.HasOne("Maestro.Data.ApplicationUser", null)
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserRole<int>", b =>
                {
                    b.HasOne("Microsoft.AspNetCore.Identity.IdentityRole<int>", null)
                        .WithMany()
                        .HasForeignKey("RoleId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("Maestro.Data.ApplicationUser", null)
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserToken<int>", b =>
                {
                    b.HasOne("Maestro.Data.ApplicationUser", null)
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });
#pragma warning restore 612, 618
        }
    }
}
