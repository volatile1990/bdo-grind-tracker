FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /source
COPY Directory.Build.props ./
COPY src/Grindcrest.Live/ src/Grindcrest.Live/
COPY src/Grindcrest.Api/ src/Grindcrest.Api/
RUN dotnet publish src/Grindcrest.Api/Grindcrest.Api.csproj -c Release -o /app --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app .
RUN mkdir /data && chown app:app /data
USER app
ENV ASPNETCORE_HTTP_PORTS=8080 Live__DatabasePath=/data/live.db
EXPOSE 8080
VOLUME /data
ENTRYPOINT ["dotnet", "Grindcrest.Api.dll"]
