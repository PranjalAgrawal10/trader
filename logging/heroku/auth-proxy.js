'use strict';

/**
 * Gate OpenSearch Dashboards with a clear HTML login form (not browser Basic Auth).
 * Credentials: DASHBOARDS_BASIC_AUTH_USER / DASHBOARDS_BASIC_AUTH_PASSWORD (Heroku config).
 */
const http = require('http');
const crypto = require('crypto');
const { URL } = require('url');

const listenPort = Number(process.env.PORT || 8080);
const user = (process.env.DASHBOARDS_BASIC_AUTH_USER || '').trim();
const pass = (process.env.DASHBOARDS_BASIC_AUTH_PASSWORD || '').trim();
const upstreamBase = process.env.DASHBOARDS_UPSTREAM || 'http://127.0.0.1:5601';
const sessionSecret = (process.env.DASHBOARDS_SESSION_SECRET || `${user}:${pass}:trader-logs`).trim();
const cookieName = 'trader_logs_auth';
const maxAgeSec = 60 * 60 * 24 * 7; // 7 days

if (!user || !pass) {
  console.error('DASHBOARDS_BASIC_AUTH_USER and DASHBOARDS_BASIC_AUTH_PASSWORD are required.');
  process.exit(1);
}

function safeEqual(a, b) {
  const aa = Buffer.from(String(a), 'utf8');
  const bb = Buffer.from(String(b), 'utf8');
  if (aa.length !== bb.length) return false;
  return crypto.timingSafeEqual(aa, bb);
}

function sign(payload) {
  return crypto.createHmac('sha256', sessionSecret).update(payload).digest('hex');
}

function makeToken() {
  const payload = `v1:${user}:${Date.now()}`;
  return `${Buffer.from(payload, 'utf8').toString('base64url')}.${sign(payload)}`;
}

function tokenValid(token) {
  if (!token || !token.includes('.')) return false;
  const [b64, sig] = token.split('.');
  let payload;
  try {
    payload = Buffer.from(b64, 'base64url').toString('utf8');
  } catch {
    return false;
  }
  if (!safeEqual(sign(payload), sig)) return false;
  const parts = payload.split(':');
  if (parts.length < 3 || parts[0] !== 'v1' || parts[1] !== user) return false;
  const ts = Number(parts[2]);
  if (!Number.isFinite(ts)) return false;
  return Date.now() - ts < maxAgeSec * 1000;
}

function parseCookies(header) {
  const out = {};
  if (!header) return out;
  for (const part of header.split(';')) {
    const idx = part.indexOf('=');
    if (idx < 0) continue;
    const k = part.slice(0, idx).trim();
    const v = part.slice(idx + 1).trim();
    out[k] = decodeURIComponent(v);
  }
  return out;
}

function isAuthed(req) {
  const cookies = parseCookies(req.headers.cookie);
  return tokenValid(cookies[cookieName]);
}

function loginPage(errorMessage) {
  const err = errorMessage
    ? `<p style="color:#b42318;margin:0 0 1rem;font-size:0.95rem">${errorMessage}</p>`
    : '';
  return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8"/>
  <meta name="viewport" content="width=device-width, initial-scale=1"/>
  <title>Trader Logs — Sign in</title>
  <style>
    :root { color-scheme: light dark; }
    body { margin:0; min-height:100vh; display:grid; place-items:center;
      font-family: ui-sans-serif, system-ui, Segoe UI, Roboto, Helvetica, Arial, sans-serif;
      background: radial-gradient(1200px 600px at 20% 0%, #1e293b 0%, #0f172a 55%, #020617 100%);
      color:#e2e8f0; }
    .card { width:min(92vw, 380px); background:rgba(15,23,42,.92); border:1px solid #334155;
      border-radius:14px; padding:1.75rem; box-shadow:0 20px 50px rgba(0,0,0,.35); }
    h1 { margin:0 0 .35rem; font-size:1.35rem; letter-spacing:.02em; }
    p.sub { margin:0 0 1.25rem; color:#94a3b8; font-size:.92rem; }
    label { display:block; font-size:.85rem; margin:0 0 .35rem; color:#cbd5e1; }
    input { width:100%; box-sizing:border-box; border-radius:8px; border:1px solid #475569;
      background:#020617; color:#f8fafc; padding:.7rem .8rem; margin:0 0 .9rem; font-size:1rem; }
    button { width:100%; border:0; border-radius:8px; padding:.75rem 1rem; font-weight:600;
      background:#38bdf8; color:#0f172a; cursor:pointer; font-size:1rem; }
    button:hover { filter:brightness(1.05); }
    .hint { margin:1rem 0 0; color:#64748b; font-size:.8rem; line-height:1.4; }
  </style>
</head>
<body>
  <form class="card" method="POST" action="/__auth/login" autocomplete="on">
    <h1>Trader Logs</h1>
    <p class="sub">Sign in to OpenSearch Dashboards</p>
    ${err}
    <label for="username">Username</label>
    <input id="username" name="username" type="text" required autofocus autocomplete="username"/>
    <label for="password">Password</label>
    <input id="password" name="password" type="password" required autocomplete="current-password"/>
    <button type="submit">Sign in</button>
    <p class="hint">Use the username/password configured on the Heroku app
      (<code>DASHBOARDS_BASIC_AUTH_*</code>). This is not the OpenSearch docs default (<code>admin</code>).</p>
  </form>
</body>
</html>`;
}

function readBody(req) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    req.on('data', (c) => chunks.push(c));
    req.on('end', () => resolve(Buffer.concat(chunks).toString('utf8')));
    req.on('error', reject);
  });
}

function parseForm(body) {
  const out = {};
  for (const part of body.split('&')) {
    if (!part) continue;
    const idx = part.indexOf('=');
    const k = decodeURIComponent((idx < 0 ? part : part.slice(0, idx)).replace(/\+/g, ' '));
    const v = decodeURIComponent((idx < 0 ? '' : part.slice(idx + 1)).replace(/\+/g, ' '));
    out[k] = v;
  }
  return out;
}

const upstream = new URL(upstreamBase);

async function handleAuthRoutes(req, res) {
  const url = new URL(req.url || '/', `http://${req.headers.host || 'localhost'}`);

  if (req.method === 'GET' && (url.pathname === '/__auth/login' || url.pathname === '/login')) {
    res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store' });
    res.end(loginPage(''));
    return true;
  }

  if (req.method === 'POST' && url.pathname === '/__auth/login') {
    const body = await readBody(req);
    const form = parseForm(body);
    if (safeEqual(form.username || '', user) && safeEqual(form.password || '', pass)) {
      const token = makeToken();
      res.writeHead(302, {
        Location: '/',
        'Set-Cookie': `${cookieName}=${encodeURIComponent(token)}; Path=/; HttpOnly; Secure; SameSite=Lax; Max-Age=${maxAgeSec}`,
        'Cache-Control': 'no-store',
      });
      res.end();
      return true;
    }
    res.writeHead(401, { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store' });
    res.end(loginPage('Invalid username or password.'));
    return true;
  }

  if (req.method === 'GET' && url.pathname === '/__auth/logout') {
    res.writeHead(302, {
      Location: '/__auth/login',
      'Set-Cookie': `${cookieName}=; Path=/; HttpOnly; Secure; SameSite=Lax; Max-Age=0`,
    });
    res.end();
    return true;
  }

  return false;
}

function proxy(req, res) {
  const headers = { ...req.headers, host: upstream.host };
  delete headers.cookie;

  const proxyReq = http.request(
    {
      protocol: upstream.protocol,
      hostname: upstream.hostname,
      port: upstream.port || 80,
      path: req.url,
      method: req.method,
      headers,
    },
    (proxyRes) => {
      res.writeHead(proxyRes.statusCode || 502, proxyRes.headers);
      proxyRes.pipe(res);
    },
  );

  proxyReq.on('error', (err) => {
    res.writeHead(502, { 'Content-Type': 'text/plain; charset=utf-8' });
    res.end(`Bad gateway: ${err.message}`);
  });

  req.pipe(proxyReq);
}

const server = http.createServer(async (req, res) => {
  try {
    if (await handleAuthRoutes(req, res)) return;

    if (!isAuthed(req)) {
      res.writeHead(302, { Location: '/__auth/login', 'Cache-Control': 'no-store' });
      res.end();
      return;
    }

    proxy(req, res);
  } catch (err) {
    res.writeHead(500, { 'Content-Type': 'text/plain; charset=utf-8' });
    res.end(`Proxy error: ${err.message}`);
  }
});

server.listen(listenPort, '0.0.0.0', () => {
  console.log(`HTML login proxy listening on 0.0.0.0:${listenPort} → ${upstreamBase} (user=${user})`);
});
