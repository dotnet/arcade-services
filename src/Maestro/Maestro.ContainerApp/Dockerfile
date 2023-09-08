#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Packages.props", "."]
COPY ["Directory.Build.props", "."]
COPY ["Directory.Build.targets", "."]
COPY ["NuGet.config", "."]
COPY ["global.json", "."]
COPY ["src/Maestro/Maestro.ContainerApp/Maestro.ContainerApp.csproj", "src/Maestro/Maestro.ContainerApp/"]
RUN dotnet restore "src/Maestro/Maestro.ContainerApp/Maestro.ContainerApp.csproj"
COPY . .
WORKDIR "/src/src/Maestro/Maestro.ContainerApp"
RUN dotnet build "Maestro.ContainerApp.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
RUN dotnet publish "Maestro.ContainerApp.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Maestro.ContainerApp.dll"]
