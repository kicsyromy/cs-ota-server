FROM mcr.microsoft.com/dotnet/sdk:7.0-alpine AS build
WORKDIR /source

COPY *.csproj .
COPY *.cs .
RUN dotnet restore
RUN dotnet publish -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:7.0-alpine
WORKDIR /app
COPY --from=build /app ./
ENTRYPOINT ["dotnet", "sc-ota-server.dll"]
