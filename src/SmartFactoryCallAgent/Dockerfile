# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["SmartFactoryCallAgent.csproj", "."]
RUN dotnet restore "SmartFactoryCallAgent.csproj"
COPY . .
RUN dotnet publish "SmartFactoryCallAgent.csproj" -c Release -o /app/publish

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SmartFactoryCallAgent.dll"]
