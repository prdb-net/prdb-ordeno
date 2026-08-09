# syntax=docker/dockerfile:1

# The image described by ADR 0013 and shipped by ADR 0011: Debian, with ffmpeg
# from the distribution, built for linux/amd64 and linux/arm64.
#
# Only the last stage is architecture-specific. The frontend build and the .NET
# publish both run on the build machine's own architecture and produce output
# that does not care where it ends up — static files in one case, portable IL in
# the other — so an arm64 image costs emulation only for `apt-get install`,
# rather than for a Node build and an MSBuild run under QEMU.


# --- The frontend -------------------------------------------------------------
# ADR 0006: Vite compiles the frontend into the host project's wwwroot, and the
# runtime stage below carries neither Node nor node_modules.

FROM --platform=$BUILDPLATFORM node:24-bookworm-slim AS frontend

# The same relative layout as the repository, so that vite.config.ts writes
# where it says it does: ../Prdb.Ordeno.Host/wwwroot.
WORKDIR /source/src/Prdb.Ordeno.Frontend

COPY src/Prdb.Ordeno.Frontend/package.json src/Prdb.Ordeno.Frontend/package-lock.json ./
RUN npm ci

COPY src/Prdb.Ordeno.Frontend/ ./
RUN npm run build


# --- The application ----------------------------------------------------------

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS backend

WORKDIR /source

# The project files first, so that restoring is cached against them rather than
# against every edit to a .cs file.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Prdb.Ordeno.Core/Prdb.Ordeno.Core.csproj src/Prdb.Ordeno.Core/
COPY src/Prdb.Ordeno.Infrastructure/Prdb.Ordeno.Infrastructure.csproj src/Prdb.Ordeno.Infrastructure/
COPY src/Prdb.Ordeno.Host/Prdb.Ordeno.Host.csproj src/Prdb.Ordeno.Host/
RUN dotnet restore src/Prdb.Ordeno.Host/Prdb.Ordeno.Host.csproj

COPY src/Prdb.Ordeno.Core/ src/Prdb.Ordeno.Core/
COPY src/Prdb.Ordeno.Infrastructure/ src/Prdb.Ordeno.Infrastructure/
COPY src/Prdb.Ordeno.Host/ src/Prdb.Ordeno.Host/
COPY --from=frontend /source/src/Prdb.Ordeno.Host/wwwroot src/Prdb.Ordeno.Host/wwwroot

# No runtime identifier: framework-dependent output runs on either architecture
# from the same publish, and the native pieces it does need — SQLite, above all
# — ship for both and are picked at startup.
#
# SkipFrontendBuild, because the stage above already did it and this image has
# no Node in it. OpenApiGenerateDocuments, because ADR 0014's document is a
# build-time artefact for the frontend's types, committed in the repository, and
# writing it again here would only load the application for nothing.
RUN dotnet publish src/Prdb.Ordeno.Host/Prdb.Ordeno.Host.csproj \
        --no-restore \
        --configuration Release \
        --output /application \
        -p:SkipFrontendBuild=true \
        -p:OpenApiGenerateDocuments=false


# --- The runtime --------------------------------------------------------------

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# ADR 0013: ffmpeg and ffprobe come from Debian rather than from a static build
# maintained here. util-linux carries setpriv, which is how the entrypoint drops
# to the user's identity without leaving a supervisor process between the
# application and the signals it has to receive.
RUN apt-get update \
    && apt-get install --yes --no-install-recommends ffmpeg util-linux \
    && rm --recursive --force /var/lib/apt/lists/*

WORKDIR /application
COPY --from=backend /application ./
COPY --chmod=0755 docker/entrypoint.sh /usr/local/bin/ordeno-entrypoint

# ADR 0009: the environment carries only what has to exist before the
# application starts. Everything else is answered in the browser on first run.
# There is deliberately no VOLUME for the data directory: an anonymous volume
# would let a forgotten mount look like it worked until the container is
# replaced and the password and configuration are gone with it.
ENV ORDENO_DATA_DIRECTORY=/data \
    ASPNETCORE_HTTP_PORTS=8080 \
    PUID=1000 \
    PGID=1000 \
    UMASK=022

EXPOSE 8080

ENTRYPOINT ["/usr/local/bin/ordeno-entrypoint"]
CMD ["dotnet", "Prdb.Ordeno.Host.dll"]
