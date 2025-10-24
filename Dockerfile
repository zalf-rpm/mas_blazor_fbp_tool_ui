# Build and run Blazor Server app (net8.0)

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app

# Expose HTTP (Kestrel defaults to 8080 in containers)
EXPOSE 8080
ENV ASPNETCORE_URLS="http://+:8080"

# Build image
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files first to cache restore layers
COPY BlazorDrawFBP.csproj ./
COPY capnproto-dotnetcore/Capnp.Net.Runtime/Capnp.Net.Runtime.csproj capnproto-dotnetcore/Capnp.Net.Runtime/
COPY mas_blazor_components/BlazorComponents.csproj mas_blazor_components/
COPY mas_capnproto_schemas/gen/csharp/zalfmas_capnpschemas.csproj mas_capnproto_schemas/gen/csharp/
COPY mas_csharp_common/common.csproj mas_csharp_common/

# Restore dependencies
RUN dotnet restore ./BlazorDrawFBP.csproj

# Copy the entire source
COPY . .

# Publish the app
RUN dotnet publish ./BlazorDrawFBP.csproj -c Release -o /app/publish /p:UseAppHost=false
RUN dotnet publish ./BlazorDrawFBP.csproj -c Debug -o /app/publish-debug /p:UseAppHost=false /p:DebugType=portable

# Final image
FROM base AS prod 
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "BlazorDrawFBP.dll"]

# Development image
FROM base AS dev
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Development \
    Logging__LogLevel__Default=Debug
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl unzip \
    && rm -rf /var/lib/apt/lists/* \
    && curl -sSL https://aka.ms/getvsdbgsh -o /tmp/getvsdbg.sh \
    && chmod +x /tmp/getvsdbg.sh \
    && /tmp/getvsdbg.sh -v latest -l /vsdbg \
    && rm /tmp/getvsdbg.sh
COPY --from=build /app/publish-debug .
ENTRYPOINT ["dotnet", "BlazorDrawFBP.dll"]
