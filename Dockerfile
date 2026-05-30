FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM node:22-alpine AS spa
WORKDIR /spa
COPY web/admin/package*.json ./
RUN npm install --no-audit --no-fund
COPY web/admin/. ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/Ingest.Api/Ingest.Api.csproj", "src/Ingest.Api/"]
COPY ["src/Ingest.Core/Ingest.Core.csproj", "src/Ingest.Core/"]
COPY ["src/Ingest.Infrastructure/Ingest.Infrastructure.csproj", "src/Ingest.Infrastructure/"]
COPY ["src/Ingest.ServiceDefaults/Ingest.ServiceDefaults.csproj", "src/Ingest.ServiceDefaults/"]
RUN dotnet restore "src/Ingest.Api/Ingest.Api.csproj"
COPY . .
COPY --from=spa /spa/dist /src/web/admin/dist
WORKDIR /src/src/Ingest.Api
RUN dotnet publish "Ingest.Api.csproj" -c Release -o /app/publish /p:BuildSpaOnPublish=false /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENTRYPOINT ["dotnet", "Ingest.Api.dll"]
