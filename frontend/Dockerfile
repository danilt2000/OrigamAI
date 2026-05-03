FROM node:20-alpine AS build
WORKDIR /app

# Install dependencies
COPY package*.json ./
RUN npm ci

# Build the app
COPY . .
RUN npm run build

# Serve with Nginx
FROM nginx:alpine
# Copy the custom Nginx config
COPY nginx.conf /etc/nginx/conf.d/default.conf 
COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 80

CMD ["nginx", "-g", "daemon off;"]