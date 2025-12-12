# BUILD STAGE
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

# Copy the entire repo
COPY . .

# Auto-detect the .csproj (instead of hardcoding)
RUN dotnet restore $(find . -name "*.csproj")

# Publish
RUN dotnet publish $(find . -name "*.csproj") -c Release -o /app/publish


# RUNTIME STAGE
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Pharma.dll"]
