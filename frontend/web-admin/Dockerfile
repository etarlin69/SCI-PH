FROM node:24-bookworm-slim AS build

WORKDIR /app

COPY ["frontend/web-admin/package.json", "frontend/web-admin/package-lock.json", "./"]

RUN npm ci \
    --no-audit \
    --no-fund \
    --fetch-retries=5 \
    --fetch-retry-mintimeout=10000 \
    --fetch-retry-maxtimeout=60000

COPY ["frontend/web-admin/", "./"]

RUN npm run build


FROM nginxinc/nginx-unprivileged:alpine AS final

WORKDIR /usr/share/nginx/html

COPY --from=build /app/dist ./
COPY ["frontend/web-admin/nginx/default.conf.template", "/etc/nginx/templates/default.conf.template"]

ENV WEB_BFF_UPSTREAM=http://web-bff:8080
ENV NGINX_ENVSUBST_FILTER=WEB_BFF_UPSTREAM

EXPOSE 8080
