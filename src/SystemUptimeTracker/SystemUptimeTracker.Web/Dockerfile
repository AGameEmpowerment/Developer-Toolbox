# syntax=docker/dockerfile:1.7

FROM node:24-alpine AS build
WORKDIR /app

ENV NEXT_TELEMETRY_DISABLED=1

RUN apk add --no-cache libc6-compat

COPY package.json ./
COPY package-lock.json ./

RUN npm ci

COPY . ./

RUN npm run build

FROM node:24-alpine AS runtime
WORKDIR /app

ENV NODE_ENV=production \
    NEXT_TELEMETRY_DISABLED=1 \
    PORT=3001 \
    PATH=/app/node_modules/.bin:$PATH

RUN apk add --no-cache libc6-compat \
    && addgroup -S appgroup \
    && adduser -S appuser -G appgroup -h /app

COPY --from=build /app/.next/standalone ./
COPY --from=build /app/.next/static ./.next/static
COPY --from=build /app/public ./public

RUN chown -R appuser:appgroup /app

EXPOSE 3001

USER appuser

CMD ["node", "server.js"]
