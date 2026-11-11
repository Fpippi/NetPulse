FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/NetPulse.Web/NetPulse.Web.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

USER root
RUN mkdir -p /data && chown $APP_UID /data
USER $APP_UID

ENV ASPNETCORE_HTTP_PORTS=8080
ENV NetPulse__DatabasePath=/data/netpulse.db
VOLUME /data
EXPOSE 8080

ENTRYPOINT ["dotnet", "NetPulse.Web.dll"]
