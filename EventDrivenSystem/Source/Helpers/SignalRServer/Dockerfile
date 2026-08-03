# See https://aka.ms/customizecontainer to learn how to customize your debug container
# and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER app
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["SignalRServer.csproj", "."]
RUN dotnet restore "./SignalRServer.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "./SignalRServer.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Install utilities in the build stage
RUN apt-get update && \
    apt-get install -y curl telnet iputils-ping && \
    rm -rf /var/lib/apt/lists/*

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./SignalRServer.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Copy utilities from build stage to final stage
COPY --from=build /usr/bin/curl /usr/bin/curl
COPY --from=build /usr/bin/telnet /usr/bin/telnet
COPY --from=build /bin/ping /bin/ping

ENTRYPOINT ["dotnet", "SignalRServer.dll"]
