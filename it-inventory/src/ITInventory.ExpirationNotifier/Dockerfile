# Builds and runs ITInventory.ExpirationNotifier as a long-running background worker
# (it schedules its own daily run internally -- see Workers/ExpirationCheckWorker.cs -- so
# this deploys as a Deployment with replicas: 1, not a CronJob/Job).
#
# Same arbitrary-non-root-UID handling as the Web Dockerfile: group-0 ownership on
# everything the process writes to (its daily text log under ./logs), no privileged ports
# involved since this process doesn't listen on the network at all.
#
# Build from the repo root (it-inventory/), matching the multi-project ProjectReference
# layout:  docker build -f src/ITInventory.ExpirationNotifier/Dockerfile -t itinventory-notifier .

# ---- build stage ----
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY src/ITInventory.Data/ITInventory.Data.csproj ITInventory.Data/
COPY src/ITInventory.ExpirationNotifier/ITInventory.ExpirationNotifier.csproj ITInventory.ExpirationNotifier/
RUN dotnet restore ITInventory.ExpirationNotifier/ITInventory.ExpirationNotifier.csproj

COPY src/ITInventory.Data/ ITInventory.Data/
COPY src/ITInventory.ExpirationNotifier/ ITInventory.ExpirationNotifier/
RUN dotnet publish ITInventory.ExpirationNotifier/ITInventory.ExpirationNotifier.csproj -c Release -o /app/publish --no-restore

# ---- runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

RUN mkdir -p /app/logs /app/home \
    && chgrp -R 0 /app \
    && chmod -R g=u /app

ENV HOME=/app/home

ENTRYPOINT ["dotnet", "ITInventory.ExpirationNotifier.dll"]
