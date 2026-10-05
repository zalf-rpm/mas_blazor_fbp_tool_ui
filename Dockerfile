# Build and run Blazor Server app (net10.0)

FROM mcr.microsoft.com/dotnet/aspnet:10.0.11 AS base
WORKDIR /app

EXPOSE 8080

RUN useradd -m -s /usr/sbin/nologin appuser

FROM mcr.microsoft.com/dotnet/sdk:10.0.400 AS build
WORKDIR /src

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    EnableSourceControlManagerQueries=false \
    EnableSourceLink=false

# Copy project, props, and lock files for restore caching
COPY Directory.Build.props .
COPY Directory.Packages.props .
COPY packages.lock.json .
COPY BlazorDrawFBP.csproj .

COPY capnproto-dotnetcore/Directory.Build.props capnproto-dotnetcore/
COPY capnproto-dotnetcore/Directory.Build.targets capnproto-dotnetcore/
COPY capnproto-dotnetcore/Directory.Packages.props capnproto-dotnetcore/
COPY capnproto-dotnetcore/version.json capnproto-dotnetcore/
COPY capnproto-dotnetcore/Capnp.Net.Runtime/Capnp.Net.Runtime.csproj capnproto-dotnetcore/Capnp.Net.Runtime/
COPY capnproto-dotnetcore/Capnp.Net.Runtime/packages.lock.json capnproto-dotnetcore/Capnp.Net.Runtime/


COPY mas_capnproto_schemas/gen/csharp/zalfmas_capnpschemas.csproj mas_capnproto_schemas/gen/csharp/
COPY mas_capnproto_schemas/gen/csharp/packages.lock.json mas_capnproto_schemas/gen/csharp/

COPY mas_csharp_common/Directory.Packages.props mas_csharp_common/
COPY mas_csharp_common/common.csproj mas_csharp_common/
COPY mas_csharp_common/packages.lock.json mas_csharp_common/

RUN dotnet restore ./BlazorDrawFBP.csproj --locked-mode

COPY . .

RUN dotnet publish ./BlazorDrawFBP.csproj -f net10.0 -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM base AS prod
WORKDIR /app
COPY --from=build --chown=appuser:appuser /app/publish .
USER appuser
ENTRYPOINT ["dotnet", "BlazorDrawFBP.dll"]

FROM build AS dev
WORKDIR /src
ENV ASPNETCORE_ENVIRONMENT=Development
ENTRYPOINT ["dotnet", "watch", "--project", "BlazorDrawFBP.csproj", "run", "--framework", "net10.0", "--no-launch-profile"]
