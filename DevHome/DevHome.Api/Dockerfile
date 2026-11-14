# Образ шаблонного API. Сборка выполняется из корня рабочей области:
#   docker build -f DevHome/DevHome.Api/Dockerfile .
#
# API публикуется только во внутренней сети Docker (порт 8080 не выводится
# на хост). Снаружи он доступен только другим контейнерам, например DevHome.
#
# Стадии:
#   source    — сборка из исходников внутри Docker;
#   published — копирование готового приложения из publish/Api.

ARG DOTNET_VERSION=11.0

# ---------------------------------------------------------------------------
# Общая среда выполнения
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime

WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# curl нужен для HEALTHCHECK и для примера вызова API изнутри контейнера.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

USER $APP_UID

HEALTHCHECK --interval=15s --timeout=5s --start-period=60s --retries=20 \
    CMD curl -fsS http://127.0.0.1:8080/health || exit 1

ENTRYPOINT ["dotnet", "DevHome.Api.dll"]

# ---------------------------------------------------------------------------
# Режим source: сборка из исходников внутри Docker
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS source-build

ARG APP_VERSION=0.1.0
WORKDIR /src

COPY . .

RUN dotnet publish "DevHome/DevHome.Api/DevHome.Api.csproj" \
    -c Release \
    -o /out \
    /p:UseAppHost=false \
    /p:Version=${APP_VERSION} \
    /p:InformationalVersion=${APP_VERSION} \
    /p:IncludeSourceRevisionInInformationalVersion=false

# ---------------------------------------------------------------------------
# Режим published: готовое приложение из publish/Api
# ---------------------------------------------------------------------------
FROM runtime AS published

ARG APP_VERSION=0.1.0
LABEL org.opencontainers.image.title="DevHome.Api" \
      org.opencontainers.image.version="${APP_VERSION}"

COPY publish/Api/ .

# ---------------------------------------------------------------------------
# Режим source: готовое приложение из стадии сборки
# ---------------------------------------------------------------------------
FROM runtime AS source

ARG APP_VERSION=0.1.0
LABEL org.opencontainers.image.title="DevHome.Api" \
      org.opencontainers.image.version="${APP_VERSION}"

COPY --from=source-build /out .
