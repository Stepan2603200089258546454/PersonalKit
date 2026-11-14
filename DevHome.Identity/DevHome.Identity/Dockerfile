# Образ DevHome.Identity. Сборка выполняется из корня рабочей области:
#   docker build -f DevHome.Identity/DevHome.Identity/Dockerfile .
#
# Два режима задаются стадией:
#   source    — исходники копируются в образ и собираются внутри Docker;
#   published — в образ копируется уже опубликованное приложение из publish/Identity.
#
# Версия передаётся аргументом APP_VERSION и попадает в номер сборки,
# тег образа и ответ /health.

ARG DOTNET_VERSION=11.0

# ---------------------------------------------------------------------------
# Общая среда выполнения
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime

WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# curl нужен для HEALTHCHECK и для ручной проверки API из контейнера.
# Каталог App_Data создаётся заранее и отдаётся пользователю приложения:
# в нём лежат ключи Data Protection и сертификаты OpenIddict.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/App_Data \
    && chown -R $APP_UID:$APP_UID /app/App_Data

USER $APP_UID

HEALTHCHECK --interval=15s --timeout=5s --start-period=90s --retries=20 \
    CMD curl -fsS http://127.0.0.1:8080/health || exit 1

ENTRYPOINT ["dotnet", "DevHome.Identity.dll"]

# ---------------------------------------------------------------------------
# Режим source: сборка из исходников внутри Docker
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS source-build

ARG APP_VERSION=0.1.0
WORKDIR /src

# Копируем всё, кроме перечисленного в .dockerignore: так автоматически
# попадают все проектные ссылки и общие библиотеки, без ручного списка.
COPY . .

RUN dotnet publish "DevHome.Identity/DevHome.Identity/DevHome.Identity.csproj" \
    -c Release \
    -o /out \
    /p:UseAppHost=false \
    /p:Version=${APP_VERSION} \
    /p:InformationalVersion=${APP_VERSION} \
    /p:IncludeSourceRevisionInInformationalVersion=false

# ---------------------------------------------------------------------------
# Режим published: готовое приложение из publish/Identity
# ---------------------------------------------------------------------------
FROM runtime AS published

ARG APP_VERSION=0.1.0
LABEL org.opencontainers.image.title="DevHome.Identity" \
      org.opencontainers.image.version="${APP_VERSION}"

COPY publish/Identity/ .

# ---------------------------------------------------------------------------
# Режим source: готовое приложение из стадии сборки
# ---------------------------------------------------------------------------
FROM runtime AS source

ARG APP_VERSION=0.1.0
LABEL org.opencontainers.image.title="DevHome.Identity" \
      org.opencontainers.image.version="${APP_VERSION}"

COPY --from=source-build /out .
