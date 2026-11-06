FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY InventoryOrderApi/InventoryOrderApi.csproj InventoryOrderApi/
RUN dotnet restore InventoryOrderApi/InventoryOrderApi.csproj
COPY InventoryOrderApi/ InventoryOrderApi/
RUN dotnet publish InventoryOrderApi/InventoryOrderApi.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "InventoryOrderApi.dll"]
