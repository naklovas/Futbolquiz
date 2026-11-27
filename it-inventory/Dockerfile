# Builds and runs ITInventory.Web for OpenShift/Kubernetes.
#
# OpenShift specifics baked in here:
#  - Runs as an arbitrary, non-root UID (OpenShift's default SCC ignores any USER set in
#    the image and assigns a random UID at deploy time, always with primary group root/0),
#    so every directory the app writes to is owned by group 0 with group read/write/execute.
#  - Listens on port 8080 (non-privileged; an arbitrary UID can't bind to ports < 1024).
#  - ASPNETCORE_FORWARDEDHEADERS_ENABLED=true makes the framework trust X-Forwarded-Proto/For
#    from the OpenShift Route/router in front of the pod, so Request.Scheme correctly reads
#    "https" even though the pod itself only ever speaks plain HTTP internally.

# ---- build stage ----
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY src/ITInventory.Data/ITInventory.Data.csproj ITInventory.Data/
COPY src/ITInventory.Web/ITInventory.Web.csproj ITInventory.Web/
RUN dotnet restore ITInventory.Web/ITInventory.Web.csproj

COPY src/ITInventory.Data/ ITInventory.Data/
COPY src/ITInventory.Web/ ITInventory.Web/
RUN dotnet publish ITInventory.Web/ITInventory.Web.csproj -c Release -o /app/publish --no-restore

# ---- runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# Data Protection key ring (see Program.cs) and the runtime user's HOME both need a
# writable, group-0 directory -- arbitrary UIDs have no home directory otherwise, which
# breaks anything that assumes one exists.
RUN mkdir -p /app/keys /app/home \
    && chgrp -R 0 /app \
    && chmod -R g=u /app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    DataProtection__KeysDirectory=/app/keys \
    HOME=/app/home

EXPOSE 8080

# No USER directive: OpenShift overrides it with a random UID anyway, and leaving it out
# means the same image also runs correctly under plain Docker/Kubernetes with `USER 1000`
# (or nothing, i.e. root) forced externally -- the group-0 permissions above cover both.
ENTRYPOINT ["dotnet", "ITInventory.Web.dll"]
