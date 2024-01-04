#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER app
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Packages.props", "."]
COPY ["Directory.Build.props", "."]
COPY ["Directory.Build.targets", "."]
COPY ["NuGet.config", "."]
COPY ["global.json", "."]
COPY ["/src/Conductor/Conductor.ServiceDefaults/Conductor.ServiceDefaults.csproj", "/src/Conductor/Conductor.ServiceDefaults/"]
COPY ["src/Conductor/Conductor.Api/Conductor.Api.csproj", "/src/Conductor/Conductor.Api/"]
RUN dotnet restore "/src/Conductor/Conductor.Api/Conductor.Api.csproj"
COPY ["src/Conductor", "/src"]
RUN dotnet build "/src/Conductor.Api/Conductor.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build --no-restore

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "/src/Conductor.Api/Conductor.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Conductor.Api.dll"]
