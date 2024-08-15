# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# Build .NET apps
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine3.20 AS build
ARG BUILD_CONFIGURATION=Release

WORKDIR /src

COPY ["Directory.Packages.props", "Directory.Build.props", "Directory.Build.targets", "NuGet.config", "global.json", "./"]
COPY ["eng/Versions.props", "./eng/"]

WORKDIR /src/Maestro
COPY ["src/Maestro/Client/src/Microsoft.DotNet.Maestro.Client.csproj",                  "./Client/src/"]
COPY ["src/Maestro/Maestro.Api.Model/Maestro.Api.Model.csproj",                         "./Maestro.Api.Model/"]
COPY ["src/Maestro/Maestro.Authentication/Maestro.Authentication.csproj",               "./Maestro.Authentication/"]
COPY ["src/Maestro/Maestro.Common/Maestro.Common.csproj",                               "./Maestro.Common/"]
COPY ["src/Maestro/Maestro.Contracts/Maestro.Contracts.csproj",                         "./Maestro.Contracts/"]
COPY ["src/Maestro/Maestro.Data/Maestro.Data.csproj",                                   "./Maestro.Data/"]
COPY ["src/Maestro/Maestro.DataProviders/Maestro.DataProviders.csproj",                 "./Maestro.DataProviders/"]
COPY ["src/Maestro/Maestro.MergePolicies/Maestro.MergePolicies.csproj",                 "./Maestro.MergePolicies/"]
COPY ["src/Maestro/Maestro.MergePolicyEvaluation/Maestro.MergePolicyEvaluation.csproj", "./Maestro.MergePolicyEvaluation/"]

WORKDIR /src
COPY ["src/Microsoft.DotNet.Darc/DarcLib/Microsoft.DotNet.DarcLib.csproj", "./Microsoft.DotNet.Darc/DarcLib/"]

WORKDIR /src/ProductConstructionService
COPY ["src/ProductConstructionService/ProductConstructionService.Api/ProductConstructionService.Api.csproj", "./ProductConstructionService.Api/"]
COPY ["src/ProductConstructionService/ProductConstructionService.ServiceDefaults/ProductConstructionService.ServiceDefaults.csproj", "./ProductConstructionService.ServiceDefaults/"]
COPY ["src/ProductConstructionService/SubscriptionTriggerer/SubscriptionTriggerer.csproj", "./SubscriptionTriggerer/"]

RUN dotnet restore "./ProductConstructionService.Api/ProductConstructionService.Api.csproj"
RUN dotnet restore "./SubscriptionTriggerer/SubscriptionTriggerer.csproj"

WORKDIR /src/Maestro
COPY ["src/Maestro/Client/src",                    "./Client/src"]
COPY ["src/Maestro/Maestro.Api.Model",             "./Maestro.Api.Model"]
COPY ["src/Maestro/Maestro.Authentication",        "./Maestro.Authentication"]
COPY ["src/Maestro/Maestro.Common",                "./Maestro.Common"]
COPY ["src/Maestro/Maestro.Contracts",             "./Maestro.Contracts"]
COPY ["src/Maestro/Maestro.Data",                  "./Maestro.Data"]
COPY ["src/Maestro/Maestro.DataProviders",         "./Maestro.DataProviders"]
COPY ["src/Maestro/Maestro.MergePolicies",         "./Maestro.MergePolicies"]
COPY ["src/Maestro/Maestro.MergePolicyEvaluation", "./Maestro.MergePolicyEvaluation"]

WORKDIR /src
COPY ["src/Microsoft.DotNet.Darc/DarcLib", "./Microsoft.DotNet.Darc/DarcLib"]

WORKDIR /src/ProductConstructionService
COPY ["src/ProductConstructionService", "."]

WORKDIR ./ProductConstructionService.Api
RUN dotnet build -c $BUILD_CONFIGURATION -o /app/build --no-restore
RUN dotnet publish -c $BUILD_CONFIGURATION -o /app/publish/ProductConstructionService /p:UseAppHost=false

WORKDIR ../SubscriptionTriggerer
RUN dotnet build -c $BUILD_CONFIGURATION -o /app/build --no-restore
RUN dotnet publish -c $BUILD_CONFIGURATION -o /app/publish/SubscriptionTriggerer /p:UseAppHost=false

# Build Angular app
FROM mcr.microsoft.com/devcontainers/typescript-node:dev-20 AS angular
WORKDIR /maestro-angular
COPY ["src/Maestro/maestro-angular", "."]
ENV NODE_OPTIONS=--openssl-legacy-provider
RUN npm install
RUN npm run build -- --prod
WORKDIR /maestro-angular/dist/maestro-angular
RUN rm stats.json bundle-report.html index.html

# Copy everything to the final image
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine3.20
RUN apk add --no-cache git
RUN git config --global user.email "dotnet-maestro[bot]@users.noreply.github.com" \
 && git config --global user.name "dotnet-maestro[bot]"

WORKDIR /app
COPY --from=build /app/publish .
COPY --from=angular /maestro-angular/dist/maestro-angular wwwroot

EXPOSE 8080
EXPOSE 8081

ENTRYPOINT ["dotnet", "./ProductConstructionService/ProductConstructionService.Api.dll"]
