# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj files and restore dependencies to optimize layer caching
COPY ["SpaBookingSystem/SpaBookingSystem.Api.csproj", "SpaBookingSystem/"]
COPY ["SpaBookingSystem.ApplicationCore/SpaBookingSystem.ApplicationCore.csproj", "SpaBookingSystem.ApplicationCore/"]
COPY ["SpaBookingSystem.DataLayer/SpaBookingSystem.DataLayer.csproj", "SpaBookingSystem.DataLayer/"]
COPY ["SpaBookingSystem.Services/SpaBookingSystem.Services.csproj", "SpaBookingSystem.Services/"]

RUN dotnet restore "SpaBookingSystem/SpaBookingSystem.Api.csproj"

# Copy the remaining source code files
COPY . .
WORKDIR "/src/SpaBookingSystem"

# Build the application
RUN dotnet build "SpaBookingSystem.Api.csproj" -c Release -o /app/build

# Stage 2: Publish
FROM build AS publish
RUN dotnet publish "SpaBookingSystem.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 3: Final Runtime Image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Expose port 8080 (default ASP.NET Core 8 port)
EXPOSE 8080

# Dynamically bind to the PORT environment variable provided by Render.
# If PORT is not set, it defaults to 8080.
ENTRYPOINT ["sh", "-c", "dotnet SpaBookingSystem.Api.dll --urls \"http://0.0.0.0:${PORT:-8080}\""]
