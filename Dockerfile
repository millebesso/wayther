# syntax=docker/dockerfile:1

# ---- Frontend build: produce the static React/Leaflet bundle ----
FROM node:22-alpine AS frontend
WORKDIR /frontend
COPY frontend/package*.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

# ---- Backend build: restore + publish the ASP.NET app ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
WORKDIR /src
COPY src/Wayther/Wayther.csproj src/Wayther/
RUN dotnet restore src/Wayther/Wayther.csproj
COPY src/ src/
RUN dotnet publish src/Wayther/Wayther.csproj -c Release -o /app

# ---- Runtime: dotnet serves both the API and the static frontend ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=backend /app ./
COPY --from=frontend /frontend/dist ./wwwroot
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Wayther.dll"]
