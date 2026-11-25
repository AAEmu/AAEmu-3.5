FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS builder
ARG CONFIGURATION
ARG GAME_DB_URL

RUN apk add --no-cache xz

WORKDIR app
COPY ./Directory.Build.props .
COPY ./Directory.Packages.props .
COPY ./AAEmu.Commons ./AAEmu.Commons
COPY ./AAEmu.Geometry ./AAEmu.Geometry
COPY ./AAEmu.Game ./AAEmu.Game
COPY ./SQL/aaemu_game.sql ./SQL/aaemu_login.sql ./SQL/
COPY ./SQL/patches/compact/startup ./SQL/patches/compact/startup
RUN dotnet publish ./AAEmu.Game/AAEmu.Game.csproj -c $CONFIGURATION -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine

ARG CONFIGURATION
ARG LOGIN_HOST
ARG LOGIN_PORT
ARG DB_HOST
ARG DB_PORT
ARG DB_USER
ARG DB_PASSWORD

RUN apk add --no-cache openssl mysql-client

WORKDIR app
COPY --from=builder /app/publish ./

EXPOSE 1239 1250
ENTRYPOINT ["dotnet", "AAEmu.Game.dll"]
