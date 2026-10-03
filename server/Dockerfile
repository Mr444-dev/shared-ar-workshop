FROM node:22-alpine
WORKDIR /app
COPY server.js ./server.js
ENV HOST=0.0.0.0
EXPOSE 10000
CMD ["sh", "-c", "node server.js --host 0.0.0.0 --port ${PORT:-10000}"]
