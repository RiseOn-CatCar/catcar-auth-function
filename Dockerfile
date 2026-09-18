# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY Directory.Build.props Directory.Packages.props CatCar.AuthFunction.slnx ./
COPY src/CatCar.AuthFunction/CatCar.AuthFunction.csproj src/CatCar.AuthFunction/
COPY src/CatCar.ServiceDefaults/CatCar.ServiceDefaults.csproj src/CatCar.ServiceDefaults/
COPY tests/CatCar.AuthFunction.Tests/CatCar.AuthFunction.Tests.csproj tests/CatCar.AuthFunction.Tests/

RUN dotnet restore CatCar.AuthFunction.slnx

COPY src/ src/
COPY tests/ tests/

RUN dotnet publish src/CatCar.AuthFunction/CatCar.AuthFunction.csproj -c Release -o /app

# Stage 2: Runtime
FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated10.0
ENV AzureWebJobsScriptRoot=/home/site/wwwroot \
    AzureFunctionsJobHost__Logging__Console__IsEnabled=true

COPY --from=build /app /home/site/wwwroot
