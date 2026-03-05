FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine3.22 AS build
ARG BUILD_CONFIGURATION=Release
ARG PACKAGE_VERSION="0.0.99-dev"

WORKDIR /src

COPY ["Directory.Packages.props", "Directory.Build.props", "Directory.Build.targets", "NuGet.config", "global.json", "./"]
COPY ["eng/Versions.props", "./eng/"]
COPY ["eng/Version.Details.props", "./eng/"]

# Maestro dependency
WORKDIR /src/Maestro
COPY ["src/Maestro/Maestro.Common/Maestro.Common.csproj", "./Maestro.Common/"]

WORKDIR /src/BuildInsights
COPY ["src/BuildInsights/BuildInsights.DummyApp/BuildInsights.DummyApp.csproj", "./BuildInsights.DummyApp/"]

RUN dotnet restore "./BuildInsights.DummyApp/BuildInsights.DummyApp.csproj"

# Copy full source for Maestro dependency
WORKDIR /src/Maestro
COPY ["src/Maestro/Maestro.Common", "./Maestro.Common"]

# Copy full source for BuildInsights
WORKDIR /src/BuildInsights
COPY ["src/BuildInsights", "."]

WORKDIR /src/BuildInsights/BuildInsights.DummyApp
RUN dotnet build -c $BUILD_CONFIGURATION -o /app/build-dummy-app --no-restore
RUN dotnet publish -c $BUILD_CONFIGURATION -o /app/publish/DummyApp /p:UseAppHost=false /p:Version=${PACKAGE_VERSION}

# Copy everything to the final image
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine3.22
RUN apk add --no-cache icu-libs

WORKDIR /app
COPY --from=build /app/publish/DummyApp ./DummyApp

EXPOSE 8080
EXPOSE 8081

ENV DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    LC_ALL=en_US.UTF-8 \
    LANG=en_US.UTF-8

WORKDIR /app/DummyApp
ENTRYPOINT ["dotnet", "./BuildInsights.DummyApp.dll"]
