## Multi-stage build for BlazorDrawFBP solution (Blazor Server, .NET 9)

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

# Build arguments (override at build time if needed)
ARG CONFIGURATION=Release
ARG PROJECT=BlazorDrawFBP.csproj
ARG SOLUTION=BlazorDrawFBP.sln

WORKDIR /build

# Copy solution & project files first to leverage layer caching for restore
COPY ${SOLUTION} ./
COPY ${PROJECT} ./
COPY mas_csharp_common/*.csproj mas_csharp_common/
COPY mas_blazor_components/BlazorComponents.csproj mas_blazor_components/
COPY mas_capnproto_schemas/gen/csharp/*.csproj mas_capnproto_schemas/gen/csharp/
COPY capnproto-dotnetcore/Capnp.Net.Runtime/*.csproj capnproto-dotnetcore/Capnp.Net.Runtime/

# Restore dependencies
RUN dotnet restore ${SOLUTION}

# Copy the remainder of the repository
COPY . .

# Publish (framework-dependent, creates native app host for simple ./BlazorDrawFBP entrypoint)
RUN dotnet publish "$PROJECT" -c $CONFIGURATION -o /app/publish \
    -p:UseAppHost=true \
    --no-self-contained

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# Copy published output
COPY --from=build /app/publish .

# Environment hardening / configuration
ENV DOTNET_EnableDiagnostics=0 \
    ASPNETCORE_URLS=http://0.0.0.0:8080 \
    TZ=UTC

# Kestrel listening port
EXPOSE 8080

# Entrypoint (native app host produced during publish)
ENTRYPOINT ["./BlazorDrawFBP"]
