# syntax=docker/dockerfile:1
#
# Build context is the repository's backend/ directory (see docker-compose.yml),
# so that Directory.Build.props and Directory.Packages.props are visible.

# --- Restore + build -------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

# Copy only what NuGet needs first, so the restore layer is cached until a
# project file or a pinned version actually changes.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/PersonalQuant.Api/PersonalQuant.Api.csproj                     src/PersonalQuant.Api/
COPY src/PersonalQuant.Application/PersonalQuant.Application.csproj     src/PersonalQuant.Application/
COPY src/PersonalQuant.Domain/PersonalQuant.Domain.csproj               src/PersonalQuant.Domain/
COPY src/PersonalQuant.Infrastructure/PersonalQuant.Infrastructure.csproj src/PersonalQuant.Infrastructure/
RUN dotnet restore src/PersonalQuant.Api/PersonalQuant.Api.csproj

COPY src/ src/
RUN dotnet publish src/PersonalQuant.Api/PersonalQuant.Api.csproj \
        --configuration Release \
        --no-restore \
        --output /app/publish

# --- Runtime ---------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
WORKDIR /app

# curl is installed solely so Docker Compose can probe /health. The ASP.NET
# runtime image ships no HTTP client, and a container health check has to run
# a command inside the container.
USER root
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# The base image ships a non-root 'app' user (uid 1654). Drop back to it.
USER $APP_UID

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    DOTNET_NOLOGO=1

EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "PersonalQuant.Api.dll"]
