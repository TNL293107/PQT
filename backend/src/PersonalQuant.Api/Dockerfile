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
COPY src/PersonalQuant.Cli/PersonalQuant.Cli.csproj                     src/PersonalQuant.Cli/
COPY src/PersonalQuant.Domain/PersonalQuant.Domain.csproj               src/PersonalQuant.Domain/
COPY src/PersonalQuant.Infrastructure/PersonalQuant.Infrastructure.csproj src/PersonalQuant.Infrastructure/
RUN dotnet restore src/PersonalQuant.Api/PersonalQuant.Api.csproj \
    && dotnet restore src/PersonalQuant.Cli/PersonalQuant.Cli.csproj

COPY src/ src/
RUN dotnet publish src/PersonalQuant.Api/PersonalQuant.Api.csproj \
        --configuration Release \
        --no-restore \
        --output /app/publish

# The operator CLI, shipped in the same image and started by 'docker compose
# exec' rather than by an entry point. It has to run with the deployment's own
# environment: a command that reached a different database, or ran under a
# different retry and call-spacing policy, than the host it is operating would
# be worse than having no command at all. Sharing the image is what makes that
# impossible to get wrong.
#
#   docker compose exec backend dotnet cli/PersonalQuant.Cli.dll provider list
#
# Its own directory, not the API's. Both projects publish an appsettings.json,
# and into one directory the second publish would overwrite the first — the API
# would silently lose its logging filters and its CORS defaults to a file
# written for a command line.
RUN dotnet publish src/PersonalQuant.Cli/PersonalQuant.Cli.csproj \
        --configuration Release \
        --no-restore \
        --output /app/publish/cli

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
