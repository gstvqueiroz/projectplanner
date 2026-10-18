# Bloco 44: a imagem do servidor de contas e IA. Construir na raiz do repositório:
#   docker build -f src/ProjectPlanner.Servidor/Dockerfile -t projectplanner-servidor .
# A configuração (e os segredos) vem das variáveis de ambiente da nuvem: ver docs/SERVIDOR.md.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/ProjectPlanner.Servidor/ProjectPlanner.Servidor.csproj src/ProjectPlanner.Servidor/
RUN dotnet restore src/ProjectPlanner.Servidor/ProjectPlanner.Servidor.csproj
COPY src/ProjectPlanner.Servidor/ src/ProjectPlanner.Servidor/
RUN dotnet publish src/ProjectPlanner.Servidor/ProjectPlanner.Servidor.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "ProjectPlanner.Servidor.dll"]
