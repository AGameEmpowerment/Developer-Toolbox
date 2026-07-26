# syntax=docker/dockerfile:1.7

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG CONFIGURATION=Production
WORKDIR /src

COPY SystemUptimeTracker.sln ./
COPY src/SystemUptimeTracker/SystemUptimeTracker.Api/SystemUptimeTracker.Api.csproj src/SystemUptimeTracker/SystemUptimeTracker.Api/
COPY src/SystemUptimeTracker/SystemUptimeTracker.Common/SystemUptimeTracker.Common.csproj src/SystemUptimeTracker/SystemUptimeTracker.Common/
COPY src/SystemUptimeTracker/SystemUptimeTracker.Data/SystemUptimeTracker.Data.csproj src/SystemUptimeTracker/SystemUptimeTracker.Data/
COPY src/SystemUptimeTracker/SystemUptimeTracker.ServiceDefaults/SystemUptimeTracker.ServiceDefaults.csproj src/SystemUptimeTracker/SystemUptimeTracker.ServiceDefaults/

RUN dotnet restore src/SystemUptimeTracker/SystemUptimeTracker.Api/SystemUptimeTracker.Api.csproj

COPY src/SystemUptimeTracker/ ./src/SystemUptimeTracker/

RUN dotnet publish src/SystemUptimeTracker/SystemUptimeTracker.Api/SystemUptimeTracker.Api.csproj \
    -c ${CONFIGURATION} \
    -o /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime-debug
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8002 \
    ASPNETCORE_HTTP_PORTS=8002 \
    DOTNET_EnableDiagnostics=1

COPY --from=build /app/publish ./

EXPOSE 8002

USER $APP_UID

ENTRYPOINT ["dotnet", "SystemUptimeTracker.Api.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8002 \
    ASPNETCORE_HTTP_PORTS=8002 \
    DOTNET_EnableDiagnostics=0

COPY --from=build /app/publish ./

EXPOSE 8002

USER $APP_UID

ENTRYPOINT ["dotnet", "SystemUptimeTracker.Api.dll"]
