# ---- Build ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ConcreteSalesRadar/ConcreteSalesRadar.csproj ConcreteSalesRadar/
RUN dotnet restore ConcreteSalesRadar/ConcreteSalesRadar.csproj

COPY . .
RUN dotnet publish ConcreteSalesRadar/ConcreteSalesRadar.csproj -c Release -o /app /p:UseAppHost=false

# ---- Runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_ENVIRONMENT=Production
# Railway inyecta PORT; la app lo lee en Program.cs. 8080 como respaldo local.
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "ConcreteSalesRadar.dll"]
