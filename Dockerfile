FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src
COPY InstagramEmbedForDiscord/InstagramEmbed.Application.csproj ./
RUN dotnet restore InstagramEmbed.Application.csproj
COPY InstagramEmbedForDiscord/ ./
RUN dotnet publish InstagramEmbed.Application.csproj -c Release -o /publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
LABEL org.opencontainers.image.source="https://github.com/b4shful/vxinstagram" \
      org.opencontainers.image.licenses="MIT"
RUN apk add --no-cache python3 py3-pip ca-certificates \
    && python3 -m venv /opt/extractor
COPY InstagramEmbedForDiscord/extractor/requirements.txt /tmp/requirements.txt
RUN /opt/extractor/bin/pip install --no-cache-dir --only-binary=:all: -r /tmp/requirements.txt \
    && /opt/extractor/bin/pip check
WORKDIR /app
COPY --from=build /publish ./
COPY LICENSE /app/LICENSE
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Extractor__Python=/opt/extractor/bin/python \
    PYTHONDONTWRITEBYTECODE=1
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD wget -q -O /dev/null http://127.0.0.1:8080/healthz || exit 1
ENTRYPOINT ["dotnet", "InstagramEmbed.Application.dll"]
