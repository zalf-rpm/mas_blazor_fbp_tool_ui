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

COPY . .

RUN dotnet restore ./BlazorDrawFBP.csproj --locked-mode
RUN dotnet publish ./BlazorDrawFBP.csproj -f net10.0 -c Release -o /app/publish /p:UseAppHost=false

FROM base AS prod
WORKDIR /app
COPY --from=build --chown=appuser:appuser /app/publish .
USER appuser
ENTRYPOINT ["dotnet", "BlazorDrawFBP.dll"]

FROM build AS dev
WORKDIR /src
ENV ASPNETCORE_ENVIRONMENT=Development
ENTRYPOINT ["dotnet", "watch", "--project", "BlazorDrawFBP.csproj", "run", "--framework", "net10.0", "--no-launch-profile"]
