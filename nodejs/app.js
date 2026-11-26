"use strict";

const fs = require("node:fs");
const http = require("node:http");
const path = require("node:path");

const publicDir = path.join(__dirname, "public");
const port = Number.parseInt(process.env.PORT || "3000", 10);
const host = process.env.HOST || "0.0.0.0";

if (!Number.isInteger(port) || port < 1 || port > 65535) {
    console.error("El valor de PORT no es un puerto válido.");
    process.exit(1);
}

const securityHeaders = {
    "X-Content-Type-Options": "nosniff",
    "X-Frame-Options": "SAMEORIGIN",
    "Referrer-Policy": "no-referrer",
    "Permissions-Policy": "camera=(), microphone=(), geolocation=()",
    "Cross-Origin-Resource-Policy": "same-origin",
    "Content-Security-Policy": "default-src 'self'; img-src 'self' data:; style-src 'self'; script-src 'none'; connect-src 'none'; form-action 'none'; base-uri 'none'; frame-ancestors 'self'",
};

const contentTypes = new Map([
    [".html", "text/html; charset=UTF-8"],
    [".css", "text/css; charset=UTF-8"],
    [".js", "text/javascript; charset=UTF-8"],
    [".json", "application/json; charset=UTF-8"],
    [".png", "image/png"],
    [".jpg", "image/jpeg"],
    [".jpeg", "image/jpeg"],
    [".gif", "image/gif"],
    [".svg", "image/svg+xml"],
    [".ico", "image/vnd.microsoft.icon"],
    [".webp", "image/webp"],
    [".woff", "font/woff"],
    [".woff2", "font/woff2"],
    [".txt", "text/plain; charset=UTF-8"],
]);

function sendText(response, statusCode, message, extraHeaders = {}) {
    const body = Buffer.from(message, "utf8");
    response.writeHead(statusCode, {
        ...securityHeaders,
        "Content-Type": "text/plain; charset=UTF-8",
        "Content-Length": body.length,
        "Cache-Control": "no-store",
        ...extraHeaders,
    });
    response.end(body);
}

function resolveRequestedFile(requestUrl) {
    let pathname;

    try {
        pathname = decodeURIComponent(new URL(requestUrl, "http://localhost").pathname);
    } catch {
        return null;
    }

    if (pathname === "/") {
        return path.join(publicDir, "index.html");
    }

    const relativePath = pathname.replace(/^\/+/, "");
    if (!relativePath || relativePath.includes("\0")) {
        return null;
    }

    const requestedPath = path.resolve(publicDir, relativePath);
    const publicRoot = publicDir.endsWith(path.sep) ? publicDir : publicDir + path.sep;

    if (requestedPath !== publicDir && !requestedPath.startsWith(publicRoot)) {
        return null;
    }

    return requestedPath;
}

function serveFile(request, response, filePath) {
    fs.stat(filePath, (error, stats) => {
        if (error || !stats.isFile()) {
            sendText(response, 404, "No encontrado");
            return;
        }

        const extension = path.extname(filePath).toLowerCase();
        const isHtml = extension === ".html";
        const headers = {
            ...securityHeaders,
            "Content-Type": contentTypes.get(extension) || "application/octet-stream",
            "Content-Length": stats.size,
            "Cache-Control": isHtml ? "no-store, no-cache, must-revalidate" : "public, max-age=3600",
        };

        response.writeHead(200, headers);

        if (request.method === "HEAD") {
            response.end();
            return;
        }

        const stream = fs.createReadStream(filePath);
        stream.on("error", () => {
            if (!response.headersSent) {
                sendText(response, 500, "Error interno");
            } else {
                response.destroy();
            }
        });
        stream.pipe(response);
    });
}

const server = http.createServer((request, response) => {
    if (request.method !== "GET" && request.method !== "HEAD") {
        sendText(response, 405, "Método no permitido", { Allow: "GET, HEAD" });
        return;
    }

    const filePath = resolveRequestedFile(request.url || "/");
    if (!filePath) {
        sendText(response, 400, "Solicitud no válida");
        return;
    }

    serveFile(request, response, filePath);
});

server.on("clientError", (error, socket) => {
    if (socket.writable) {
        socket.end("HTTP/1.1 400 Bad Request\r\nConnection: close\r\n\r\n");
    }
});

server.listen(port, host, () => {
    console.log(`Portal informativo disponible en http://${host}:${port}`);
});

function shutdown(signal) {
    console.log(`Cerrando el servidor por ${signal}.`);
    server.close(() => process.exit(0));
    setTimeout(() => process.exit(1), 10000).unref();
}

process.on("SIGTERM", () => shutdown("SIGTERM"));
process.on("SIGINT", () => shutdown("SIGINT"));
