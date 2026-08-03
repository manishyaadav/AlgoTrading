# Use the .NET 8.0 SDK image to build the function app
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS installer-env

COPY . /src/dotnet-function-app
RUN cd /src/dotnet-function-app && \
    mkdir -p /home/site/wwwroot && \
    dotnet publish *.csproj --output /home/site/wwwroot

# Use the Azure Functions runtime image for .NET 8.0
FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-nightly-dotnet-isolated8.0 AS runtime
ENV AzureWebJobsScriptRoot=/home/site/wwwroot \
    AzureFunctionsJobHost__Logging__Console__IsEnabled=true

# Copy the built app to the wwwroot directory
COPY --from=installer-env ["/home/site/wwwroot", "/home/site/wwwroot"]
