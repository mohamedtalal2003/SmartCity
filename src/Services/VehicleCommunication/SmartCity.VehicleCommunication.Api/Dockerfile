FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props SmartCity.sln ./
COPY src/Services/VehicleCommunication/SmartCity.VehicleCommunication.Api/SmartCity.VehicleCommunication.Api.csproj src/Services/VehicleCommunication/SmartCity.VehicleCommunication.Api/
COPY src/Services/VehicleCommunication/SmartCity.VehicleCommunication.Application/SmartCity.VehicleCommunication.Application.csproj src/Services/VehicleCommunication/SmartCity.VehicleCommunication.Application/
COPY src/Services/VehicleCommunication/SmartCity.VehicleCommunication.Domain/SmartCity.VehicleCommunication.Domain.csproj src/Services/VehicleCommunication/SmartCity.VehicleCommunication.Domain/
COPY src/Services/VehicleCommunication/SmartCity.VehicleCommunication.Infrastructure/SmartCity.VehicleCommunication.Infrastructure.csproj src/Services/VehicleCommunication/SmartCity.VehicleCommunication.Infrastructure/
COPY src/SmartCity.Contracts/SmartCity.Contracts.csproj src/SmartCity.Contracts/
COPY src/SmartCity.Contracts.Grpc/SmartCity.Contracts.Grpc.csproj src/SmartCity.Contracts.Grpc/
COPY src/BuildingBlocks/SmartCity.BuildingBlocks/SmartCity.BuildingBlocks.csproj src/BuildingBlocks/SmartCity.BuildingBlocks/
RUN dotnet restore src/Services/VehicleCommunication/SmartCity.VehicleCommunication.Api/SmartCity.VehicleCommunication.Api.csproj

COPY src/Services/VehicleCommunication/ src/Services/VehicleCommunication/
COPY src/SmartCity.Contracts/ src/SmartCity.Contracts/
COPY src/SmartCity.Contracts.Grpc/ src/SmartCity.Contracts.Grpc/
COPY src/BuildingBlocks/SmartCity.BuildingBlocks/ src/BuildingBlocks/SmartCity.BuildingBlocks/
RUN dotnet publish src/Services/VehicleCommunication/SmartCity.VehicleCommunication.Api/SmartCity.VehicleCommunication.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "SmartCity.VehicleCommunication.Api.dll"]
