FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine3.23 AS build
ARG BUILD_CONFIGURATION=Release
ARG PACKAGE_VERSION="0.0.99-dev"

WORKDIR /src

COPY ["Directory.Packages.props", "Directory.Build.props", "Directory.Build.targets", "NuGet.config", "global.json", "./"]
COPY ["eng/Versions.props", "./eng/"]
COPY ["eng/Version.Details.props", "./eng/"]

# Maestro dependency
WORKDIR /src/Maestro
COPY ["src/Maestro/Maestro.Common/Maestro.Common.csproj", "./Maestro.Common/"]
COPY ["src/Maestro/Maestro.Common/Maestro.Services.Common.csproj", "./Maestro.Services.Common/"]

# ProductConstructionService dependencies
WORKDIR /src/ProductConstructionService
COPY ["src/ProductConstructionService/ProductConstructionService.WorkItems/ProductConstructionService.WorkItems.csproj", "./ProductConstructionService.WorkItems/"]

# BuildInsights projects
WORKDIR /src/BuildInsights
COPY ["src/BuildInsights/BuildInsights.Api/BuildInsights.Api.csproj", "./BuildInsights.Api/"]
COPY ["src/BuildInsights/BuildInsights.AzureStorage.Cache/BuildInsights.AzureStorage.Cache.csproj", "./BuildInsights.AzureStorage.Cache/"]
COPY ["src/BuildInsights/BuildInsights.BuildAnalysis/BuildInsights.BuildAnalysis.csproj", "./BuildInsights.BuildAnalysis/"]
COPY ["src/BuildInsights/BuildInsights.Data/BuildInsights.Data.csproj", "./BuildInsights.Data/"]
COPY ["src/BuildInsights/BuildInsights.GitHub/BuildInsights.GitHub.csproj", "./BuildInsights.GitHub/"]
COPY ["src/BuildInsights/BuildInsights.GitHubGraphQL/BuildInsights.GitHubGraphQL.csproj", "./BuildInsights.GitHubGraphQL/"]
COPY ["src/BuildInsights/BuildInsights.KnownIssues/BuildInsights.KnownIssues.csproj", "./BuildInsights.KnownIssues/"]
COPY ["src/BuildInsights/BuildInsights.KnownIssuesMonitor/BuildInsights.KnownIssuesMonitor.csproj", "./BuildInsights.KnownIssuesMonitor/"]
COPY ["src/BuildInsights/BuildInsights.QueueInsights/BuildInsights.QueueInsights.csproj", "./BuildInsights.QueueInsights/"]
COPY ["src/BuildInsights/BuildInsights.ServiceDefaults/BuildInsights.ServiceDefaults.csproj", "./BuildInsights.ServiceDefaults/"]
COPY ["src/BuildInsights/BuildInsights.Utilities.AzureDevOps/BuildInsights.Utilities.AzureDevOps.csproj", "./BuildInsights.Utilities.AzureDevOps/"]

RUN dotnet restore "./BuildInsights.Api/BuildInsights.Api.csproj"
RUN dotnet restore "./BuildInsights.KnownIssuesMonitor/BuildInsights.KnownIssuesMonitor.csproj"

# Copy full source for Maestro dependency
WORKDIR /src/Maestro
COPY ["src/Maestro/Maestro.Common", "./Maestro.Common"]

# Copy full source for ProductConstructionService dependencies
WORKDIR /src/ProductConstructionService
COPY ["src/ProductConstructionService/ProductConstructionService.Common", "./ProductConstructionService.Common"]
COPY ["src/ProductConstructionService/ProductConstructionService.WorkItems", "./ProductConstructionService.WorkItems"]

# Copy full source for BuildInsights
WORKDIR /src/BuildInsights
COPY ["src/BuildInsights", "."]

WORKDIR /src/BuildInsights/BuildInsights.Api
RUN dotnet build -c $BUILD_CONFIGURATION -o /app/build --no-restore
RUN dotnet publish -c $BUILD_CONFIGURATION -o /app/publish/BuildInsights /p:UseAppHost=false /p:Version=${PACKAGE_VERSION}

WORKDIR /src/BuildInsights/BuildInsights.KnownIssuesMonitor
RUN dotnet build -c $BUILD_CONFIGURATION -o /app/build --no-restore
RUN dotnet publish -c $BUILD_CONFIGURATION -o /app/publish/BuildInsights.KnownIssuesMonitor /p:UseAppHost=false /p:Version=${PACKAGE_VERSION}

# Copy everything to the final image
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine3.22
RUN apk add --no-cache icu-libs

WORKDIR /app
COPY --from=build /app/publish/BuildInsights ./BuildInsights
COPY --from=build /app/publish/BuildInsights.KnownIssuesMonitor ./BuildInsights.KnownIssuesMonitor

EXPOSE 8080
EXPOSE 8081

ENV DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    LC_ALL=en_US.UTF-8 \
    LANG=en_US.UTF-8

WORKDIR /app/BuildInsights
ENTRYPOINT ["dotnet", "./BuildInsights.Api.dll"]
