#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
RUN apt-get update
RUN apt-get install git -y
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Directory.Packages.props", "Directory.Build.props", "Directory.Build.targets", "NuGet.config", "global.json", "./"]
COPY ["eng/Versions.props", "./eng/"]

COPY ["src/Maestro/Client/src/Microsoft.DotNet.Maestro.Client.csproj", "./Maestro/Client/src/"]
COPY ["src/Maestro/Maestro.Authentication/Maestro.Authentication.csproj", "./Maestro/Maestro.Authentication/"]
COPY ["src/Maestro/Maestro.AzureDevOps/Maestro.AzureDevOps.csproj", "./Maestro/Maestro.AzureDevOps/"]
COPY ["src/Maestro/Maestro.Data/Maestro.Data.csproj", "./Maestro/Maestro.Data/"]
COPY ["src/Maestro/Maestro.DataProviders/Maestro.DataProviders.csproj", "./Maestro/Maestro.DataProviders/"]
COPY ["src/Maestro/Maestro.MergePolicyEvaluation/Maestro.MergePolicyEvaluation.csproj", "./Maestro/Maestro.MergePolicyEvaluation/"]

COPY ["src/Microsoft.DotNet.Darc/DarcLib/Microsoft.DotNet.DarcLib.csproj", "./Microsoft.DotNet.Darc/DarcLib/"]

COPY ["src/ProductConstructionService/ProductConstructionService.Api/ProductConstructionService.Api.csproj", "./ProductConstructionService/ProductConstructionService.Api/"]
COPY ["src/ProductConstructionService/ProductConstructionService.ServiceDefaults/ProductConstructionService.ServiceDefaults.csproj", "./ProductConstructionService/ProductConstructionService.ServiceDefaults/"]

RUN dotnet restore "./ProductConstructionService/ProductConstructionService.Api/ProductConstructionService.Api.csproj"

COPY ["src/Maestro/Client/src", "./Maestro/Client/src"]
COPY ["src/Maestro/Maestro.Authentication", "./Maestro/Maestro.Authentication"]
COPY ["src/Maestro/Maestro.AzureDevOps", "./Maestro/Maestro.AzureDevOps"]
COPY ["src/Maestro/Maestro.Data", "./Maestro/Maestro.Data"]
COPY ["src/Maestro/Maestro.DataProviders", "./Maestro/Maestro.DataProviders"]
COPY ["src/Maestro/Maestro.MergePolicyEvaluation", "./Maestro/Maestro.MergePolicyEvaluation"]

COPY ["src/Microsoft.DotNet.Darc/DarcLib", "./Microsoft.DotNet.Darc/DarcLib"]

COPY ["src/ProductConstructionService", "./ProductConstructionService"]

RUN dotnet build "./ProductConstructionService/ProductConstructionService.Api/ProductConstructionService.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build --no-restore

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./ProductConstructionService/ProductConstructionService.Api/ProductConstructionService.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
RUN git config --global user.email "dotnet-maestro[bot]@users.noreply.github.com" \
 && git config --global user.name "dotnet-maestro[bot]"
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ProductConstructionService.Api.dll"]