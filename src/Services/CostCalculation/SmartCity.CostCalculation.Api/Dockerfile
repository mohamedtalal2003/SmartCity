FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props SmartCity.sln ./
COPY src/Services/CostCalculation/SmartCity.CostCalculation.Api/SmartCity.CostCalculation.Api.csproj src/Services/CostCalculation/SmartCity.CostCalculation.Api/
COPY src/SmartCity.Contracts/SmartCity.Contracts.csproj src/SmartCity.Contracts/
COPY src/SmartCity.Contracts.Grpc/SmartCity.Contracts.Grpc.csproj src/SmartCity.Contracts.Grpc/
COPY src/BuildingBlocks/SmartCity.BuildingBlocks/SmartCity.BuildingBlocks.csproj src/BuildingBlocks/SmartCity.BuildingBlocks/
RUN dotnet restore src/Services/CostCalculation/SmartCity.CostCalculation.Api/SmartCity.CostCalculation.Api.csproj

COPY src/Services/CostCalculation/SmartCity.CostCalculation.Api/ src/Services/CostCalculation/SmartCity.CostCalculation.Api/
COPY src/SmartCity.Contracts/ src/SmartCity.Contracts/
COPY src/SmartCity.Contracts.Grpc/ src/SmartCity.Contracts.Grpc/
COPY src/BuildingBlocks/SmartCity.BuildingBlocks/ src/BuildingBlocks/SmartCity.BuildingBlocks/
RUN dotnet publish src/Services/CostCalculation/SmartCity.CostCalculation.Api/SmartCity.CostCalculation.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "SmartCity.CostCalculation.Api.dll"]
