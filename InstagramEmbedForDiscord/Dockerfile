# ─────────────────────────────────────────────────────────────────────────────
# Stage 1 – Install snapsave Node dependencies
# ─────────────────────────────────────────────────────────────────────────────
FROM node:22-alpine AS node-build

WORKDIR /snapsave
COPY snapsave/package.json ./
RUN npm install --omit=dev
COPY snapsave/index.js ./



FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS dotnet-build

WORKDIR /src

COPY InstagramEmbed.Application.csproj ./
RUN dotnet restore InstagramEmbed.Application.csproj

COPY . ./

RUN dotnet publish InstagramEmbed.Application.csproj \
    -c Release \
    -o /app/publish \
    --no-restore


FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime

RUN apk add --no-cache nodejs

WORKDIR /app

COPY --from=dotnet-build /app/publish ./

RUN rm -rf ./snapsave
COPY --from=node-build /snapsave ./snapsave

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV SNAPSAVE_PORT=3200

ENTRYPOINT ["dotnet", "InstagramEmbed.Application.dll"]