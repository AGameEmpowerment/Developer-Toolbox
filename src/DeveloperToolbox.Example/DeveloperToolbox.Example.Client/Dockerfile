# Local-development template using only the public Docker Hub Node.js image.
# This major matches the engine used for the frontend build and verification.
FROM node:26-bookworm-slim AS dependencies
WORKDIR /app
COPY package.json package-lock.json .npmrc ./
COPY scripts/apply-braces-depth-guard.mjs ./scripts/
RUN npm ci

FROM dependencies AS build
COPY . .
RUN npm run verify && npm run build

FROM node:26-bookworm-slim AS runtime
ENV NODE_ENV=production
WORKDIR /app
COPY package.json package-lock.json .npmrc ./
COPY scripts/apply-braces-depth-guard.mjs ./scripts/
RUN npm ci --omit=dev && npm cache clean --force
COPY --from=build /app/.next ./.next
COPY --from=build /app/next.config.mjs ./next.config.mjs
RUN chown -R node:node /app
EXPOSE 3000
USER node
CMD ["npm", "start"]
