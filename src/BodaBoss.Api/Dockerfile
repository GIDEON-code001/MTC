FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/BodaBoss.Api/BodaBoss.Api.csproj src/BodaBoss.Api/
RUN dotnet restore src/BodaBoss.Api/BodaBoss.Api.csproj
COPY src/BodaBoss.Api/ src/BodaBoss.Api/
RUN dotnet publish src/BodaBoss.Api/BodaBoss.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish .
COPY database/init.sql /app/database/init.sql
USER $APP_UID
ENTRYPOINT ["dotnet", "BodaBoss.Api.dll"]
