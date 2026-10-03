const http = require('http'), fs = require('fs'), path = require('path'), crypto = require('crypto');
const { spawn } = require('child_process');
const rootsIn = (() => { try { const a = JSON.parse(process.env.GG_ROOTS || ''); if (Array.isArray(a) && a.length) return a; } catch {} return [process.env.GG_ROOT]; })();
const mounts = [];
for (const p of rootsIn) {
  if (!p) continue;
  const dir = path.resolve(p);
  if (mounts.some(m => m.dir.toLowerCase() === dir.toLowerCase())) continue;
  let base = path.basename(dir) || dir.replace(/[:\\\/]/g, '') || 'drive', name = base, n = 1;
  while (mounts.some(m => m.name.toLowerCase() === name.toLowerCase())) name = base + ' (' + (++n) + ')';
  mounts.push({ name, dir, sep: dir.endsWith(path.sep) ? dir : dir + path.sep, trash: path.join(dir, '.trash') });
}
if (!mounts.length) { console.log('ERROR: no shared folder configured. Set GG_ROOT=<folder> (or GG_ROOTS=["folder1","folder2"]) and run again.'); process.exit(1); }
const multi = mounts.length > 1;
function resolveRel(rel) {
  if (!multi) { const m = mounts[0], full = path.resolve(path.join(m.dir, rel)); return (full !== m.dir && !full.startsWith(m.sep)) ? { bad: true } : { full, m }; }
  const parts = rel.split('/').filter(Boolean);
  if (!parts.length) return { virtual: true };
  const m = mounts.find(x => x.name === parts[0]);
  if (!m) {   // old-style link without the folder name: use the first shared folder that has that file
    for (const mm of mounts) { const f = path.resolve(path.join(mm.dir, parts.join('/'))); if ((f === mm.dir || f.startsWith(mm.sep)) && fs.existsSync(f)) return { full: f, m: mm }; }
    return { missing: true };
  }
  const full = path.resolve(path.join(m.dir, parts.slice(1).join('/')));
  return (full !== m.dir && !full.startsWith(m.sep)) ? { bad: true } : { full, m };
}
const port = parseInt(process.env.GG_PORT, 10) || 8080, uiFile = process.env.GG_UI || path.join(__dirname, 'index.html');
const VIEW = process.env.GG_VIEW || '', ADMIN = process.env.GG_ADMIN || '';
let RO = process.env.GG_RO === '1';
const MANAGED = process.env.GG_MANAGED === '1';   // started by the GGShare app (it can restart us)
const MIME = { html:'text/html; charset=utf-8', txt:'text/plain; charset=utf-8', pdf:'application/pdf', svg:'image/svg+xml', jpg:'image/jpeg', jpeg:'image/jpeg', png:'image/png', gif:'image/gif', webp:'image/webp',
  mp4:'video/mp4', mkv:'video/x-matroska', webm:'video/webm', mov:'video/quicktime', avi:'video/x-msvideo', mp3:'audio/mpeg', wav:'audio/wav', ogg:'audio/ogg', flac:'audio/flac', zip:'application/zip', vtt:'text/vtt' };
const dg = s => crypto.createHash('sha256').update(s).digest();
const same = (a, b) => !!a && !!b && crypto.timingSafeEqual(dg(a), dg(b));
const sessions = new Map(), fails = new Map(), shares = new Map(), devices = new Map();
let up = 0, down = 0;
const started = Date.now();
const send = (res, code, msg) => { res.writeHead(code, { 'Content-Type': 'text/plain; charset=utf-8' }); res.end(msg); };
const json = (res, o, code) => { res.writeHead(code || 200, { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' }); res.end(JSON.stringify(o)); };
const clean = n => n.replace(/[<>:"\/\\|?*\x00-\x1f]/g, '_').replace(/^\.+/, '_');
const cookie = req => { const m = /(?:^|;\s*)gg=([a-f0-9]+)/.exec(req.headers.cookie || ''); return m && m[1]; };
const blocked = ip => { const f = fails.get(ip); return !!f && f.n >= 5 && Date.now() - f.t < 600000; };
function tryPw(ip, pw) {
  if (!pw || blocked(ip)) return null;
  if (ADMIN && same(pw, ADMIN)) { fails.delete(ip); return 'admin'; }
  if (VIEW && same(pw, VIEW)) { fails.delete(ip); return 'viewer'; }
  const f = fails.get(ip) || { n: 0, t: 0 };
  if (Date.now() - f.t > 600000) f.n = 0;
  f.n++; f.t = Date.now(); fails.set(ip, f);
  return null;
}
const LOGIN = ['<!DOCTYPE html><html><head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>GGSTUDIOS Login</title>',
  '<style>body{background:#0a0e1a;color:#e8edf5;font-family:system-ui,sans-serif;display:flex;align-items:center;justify-content:center;min-height:100vh;margin:0}',
  'form{background:#141824;border:1px solid #1f2535;border-radius:14px;padding:32px;width:300px;text-align:center}',
  'h1{background:linear-gradient(135deg,#00d9ff,#00a3cc);-webkit-background-clip:text;-webkit-text-fill-color:transparent;margin:0 0 6px}',
  'input,button{width:100%;box-sizing:border-box;padding:12px;border-radius:10px;border:1px solid #1f2535;font-size:15px;margin-top:14px}',
  'input{background:#0a0e1a;color:#e8edf5}button{background:#00d9ff;color:#00131a;font-weight:700;cursor:pointer}p{color:#ff6b6b;font-size:13px;min-height:16px}</style></head>',
  '<body><form method="POST" action="/__login"><h1>GGSTUDIOS</h1><div style="color:#8a92a8;font-size:13px">Enter password to continue</div>',
  '<input type="password" name="pw" placeholder="Password" autofocus><button>Unlock</button><p>{MSG}</p></form></body></html>'].join('');
const uaName = ua => /VLC/i.test(ua) ? 'VLC' : /iPhone|iPad/i.test(ua) ? 'iPhone/iPad' : /Android/i.test(ua) ? 'Android' : /Windows/i.test(ua) ? 'Windows PC' : /Mac/i.test(ua) ? 'Mac' : /Linux/i.test(ua) ? 'Linux' : 'Unknown';

for (const m of mounts) { try { if (!fs.existsSync(m.dir)) { console.log('NOTE: folder not available right now: ' + m.dir); continue; } fs.mkdirSync(m.trash, { recursive: true }); for (const n of fs.readdirSync(m.trash)) { const t = parseInt(n, 10); if (t && Date.now() - t > 30 * 864e5) fs.rmSync(path.join(m.trash, n), { recursive: true, force: true }); } } catch {} }

function playerHtml(name, src) {
  const safe = name.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');
  return [
    '<!DOCTYPE html><html><head><meta charset="UTF-8">',
    '<meta name="viewport" content="width=device-width, initial-scale=1.0">',
    '<title>' + safe + '</title><style>',
    'body{margin:0;background:#0a0e1a;color:#e8edf5;font-family:system-ui,sans-serif;display:flex;flex-direction:column;min-height:100vh}',
    '.bar{padding:12px 16px;display:flex;gap:10px;align-items:center;flex-wrap:wrap;border-bottom:1px solid #1f2535}',
    '.name{flex:1;font-size:14px;word-break:break-all}',
    'a,button{background:#141824;color:#00d9ff;border:1px solid #1f2535;border-radius:8px;padding:8px 14px;font-size:13px;text-decoration:none;cursor:pointer}',
    'a:hover,button:hover{border-color:#00d9ff}',
    'video{width:100%;flex:1;max-height:calc(100vh - 110px);background:#000}',
    '#msg{padding:10px 16px;font-size:13px;color:#ffb84d;min-height:18px}',
    '</style></head><body>',
    '<div class="bar"><a href="./">&larr; Back</a><div class="name">' + safe + '</div>',
    '<button id="cbtn">Compatible mode</button><button id="copybtn">Copy direct link</button><a href="' + src + '?download">Download</a></div>',
    '<div id="msg"></div>',
    '<video id="v" controls autoplay playsinline></video>',
    '<script>',
    'var v=document.getElementById("v"),msg=document.getElementById("msg"),src="' + src + '",compat=false;',
    'function useCompat(){compat=true;msg.style.color="#8a92a8";msg.textContent="Compatible mode: converting live (seeking is limited)...";v.src=src+"?transcode";v.play();}',
    'document.getElementById("cbtn").onclick=useCompat;',
    'document.getElementById("copybtn").onclick=function(){var l=location.origin+src;var b=this;function ok(){b.textContent="Copied!";setTimeout(function(){b.textContent="Copy direct link";},1500);}if(navigator.clipboard&&window.isSecureContext){navigator.clipboard.writeText(l).then(ok);}else{var t=document.createElement("textarea");t.value=l;document.body.appendChild(t);t.select();document.execCommand("copy");t.remove();ok();}};',
    'function check(){if(compat)return;if(v.videoWidth===0){msg.textContent="Your browser cannot decode this video codec (audio only). Switching to compatible mode...";setTimeout(useCompat,1200);}}',
    'v.addEventListener("loadedmetadata",check);',
    'v.addEventListener("error",function(){if(!compat){msg.textContent="Browser cannot play this format. Switching to compatible mode...";setTimeout(useCompat,1200);}else{msg.style.color="#ff6b6b";msg.textContent="Compatible mode failed. Is ffmpeg installed? Otherwise use Download and open in VLC.";}});',
    'v.src=src;var k="pos:"+src;v.addEventListener("loadedmetadata",function(){var t=+localStorage.getItem(k);if(t>5&&!compat)v.currentTime=t;});setInterval(function(){if(v.currentTime>5&&!compat)localStorage.setItem(k,v.currentTime);},3000);',
    '</script></body></html>'
  ].join('\n');
}

function transcode(full, req, res) {
  const args = ['-hide_banner','-loglevel','error','-i',full,
    '-vf','scale=-2:min(ih\\,1080)','-c:v','libx264','-preset','veryfast','-crf','23','-pix_fmt','yuv420p',
    '-c:a','aac','-b:a','192k','-ac','2',
    '-movflags','frag_keyframe+empty_moov+default_base_moof','-f','mp4','pipe:1'];
  let ff;
  try { ff = spawn('ffmpeg', args); } catch { return send(res, 500, 'ffmpeg not found'); }
  ff.on('error', () => { if (!res.headersSent) send(res, 500, 'ffmpeg not found. Install it: winget install ffmpeg'); else res.destroy(); });
  res.writeHead(200, { 'Content-Type': 'video/mp4', 'Cache-Control': 'no-store' });
  ff.stdout.on('data', d => { down += d.length; });
  ff.stdout.pipe(res);
  ff.stderr.on('data', d => console.log('[ffmpeg] ' + d));
  res.on('close', () => ff.kill('SIGKILL'));
}


function serveFile(req, res, u, full, st, dev) {
  const ext = path.extname(full).slice(1).toLowerCase();
  const headers = { 'Content-Type': MIME[ext] || 'application/octet-stream', 'Accept-Ranges': 'bytes', 'Last-Modified': st.mtime.toUTCString(), 'Cache-Control': 'no-cache' };
  if (u.searchParams.has('download')) headers['Content-Disposition'] = 'attachment; filename="' + encodeURIComponent(path.basename(full)) + '"';
  let start = 0, end = st.size - 1, code = 200;
  const m = /bytes=(\d*)-(\d*)/.exec(req.headers.range || '');
  if (m && (m[1] || m[2])) {
    if (m[1]) { start = parseInt(m[1], 10); if (m[2]) end = Math.min(parseInt(m[2], 10), end); } else start = Math.max(0, st.size - parseInt(m[2], 10));
    if (start > end || start >= st.size) { res.writeHead(416, { 'Content-Range': 'bytes */' + st.size }); return res.end(); }
    code = 206; headers['Content-Range'] = 'bytes ' + start + '-' + end + '/' + st.size;
  }
  headers['Content-Length'] = end - start + 1;
  res.writeHead(code, headers);
  if (req.method === 'HEAD' || st.size === 0) return res.end();
  if (start === 0 && !u.searchParams.has('thumb')) console.log('DOWNLOAD ' + path.basename(full) + ' -> ' + dev.ip);
  const s = fs.createReadStream(full, { start, end });
  s.on('data', d => { down += d.length; dev.bytes += d.length; });
  s.on('error', () => res.destroy());
  res.on('close', () => s.destroy());
  s.pipe(res);
}


// ================= Live screen share (ffmpeg gdigrab -> fragmented MP4 -> browsers via MSE) =================
let cap = null, initSeg = null, moofBuf = null, pend = Buffer.alloc(0), curFps = 60, curBr = '15M';
const viewers = new Set();
const SCREEN_HTML = `<!DOCTYPE html><html><head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Live Screen</title>
<style>html,body{margin:0;height:100%;background:#000;color:#8a92a8;font-family:system-ui,sans-serif}video{width:100vw;height:100vh;background:#000;object-fit:contain}
#m{position:fixed;top:12px;left:12px;background:rgba(10,14,26,.85);padding:8px 14px;border-radius:8px;font-size:13px;z-index:2}</style></head>
<body><div id="m">Connecting...</div><video id="v" autoplay muted playsinline controls></video>
<script>
var v=document.getElementById("v"),m=document.getElementById("m");
function msg(t){m.style.display=t?"block":"none";m.textContent=t||"";}
function go(){
  if(!window.MediaSource){msg("This browser does not support live video");return;}
  var ms=new MediaSource(),q=[],sb=null,dead=false;v.src=URL.createObjectURL(ms);
  function retry(t){if(dead)return;dead=true;msg(t||"Waiting for host to share screen...");setTimeout(go,2000);}
  function pump(){if(sb&&!sb.updating&&q.length){try{sb.appendBuffer(q.shift());}catch(e){q=[];}}}
  ms.addEventListener("sourceopen",function(){
    try{sb=ms.addSourceBuffer('video/mp4; codecs="avc1.640033"');}catch(e){msg("Video format not supported");return;}
    sb.mode="sequence";
    sb.addEventListener("updateend",function(){
      if(v.buffered.length){var e=v.buffered.end(v.buffered.length-1);
        if(e-v.currentTime>0.7)v.currentTime=e-0.15;
        if(!sb.updating&&v.buffered.start(0)<e-8)try{sb.remove(0,e-4);}catch(x){}}
      pump();});
    fetch("/__screen/stream").then(function(r){
      if(!r.ok){retry();return;}
      msg("");var rd=r.body.getReader();
      (function read(){rd.read().then(function(x){if(x.done){retry("Stream ended - reconnecting...");return;}q.push(x.value);pump();read();}).catch(function(){retry("Connection lost - reconnecting...");});})();
    }).catch(function(){retry();});
  });
}
v.addEventListener("click",function(){v.muted=false;v.play();});
go();
</script></body></html>`;

function stopScreen() {
  if (cap) { try { cap.kill('SIGKILL'); } catch {} cap = null; }
  for (const r of viewers) { try { r.end(); } catch {} }
  viewers.clear(); initSeg = null; moofBuf = null; pend = Buffer.alloc(0);
  console.log('SCREEN sharing stopped');
}
function parseBoxes() {
  while (pend.length >= 8) {
    const size = pend.readUInt32BE(0);
    if (size < 8 || pend.length < size) break;
    const type = pend.toString('latin1', 4, 8), box = pend.subarray(0, size);
    pend = pend.subarray(size);
    if (type === 'ftyp' || type === 'moov') initSeg = Buffer.concat([initSeg || Buffer.alloc(0), box]);
    else if (type === 'moof') moofBuf = box;
    else if (type === 'mdat' && moofBuf) {
      const frag = Buffer.concat([moofBuf, box]); moofBuf = null;
      for (const r of viewers) { if (r.writableLength > 6e6) { r.destroy(); viewers.delete(r); } else r.write(frag); }
    }
  }
}
function startScreen(forceEnc) {
  if (cap) return;
  const enc = forceEnc || process.env.GG_ENC || 'libx264', br = process.env.GG_BR || '15M', fps = process.env.GG_FPS || '60';
  curFps = +fps; curBr = br;
  const ev = enc === 'h264_nvenc' ? ['-c:v', 'h264_nvenc', '-preset', 'p1', '-tune', 'll', '-rc', 'cbr']
    : enc === 'h264_qsv' ? ['-c:v', 'h264_qsv', '-preset', 'veryfast']
    : enc === 'h264_amf' ? ['-c:v', 'h264_amf', '-usage', 'ultralowlatency']
    : ['-c:v', 'libx264', '-preset', 'ultrafast', '-tune', 'zerolatency'];
  const args = ['-hide_banner', '-loglevel', 'error', '-f', 'gdigrab', '-framerate', fps, '-draw_mouse', '1', '-i', 'desktop',
    '-vf', 'crop=trunc(iw/2)*2:trunc(ih/2)*2', ...ev, '-b:v', br, '-maxrate', br, '-bufsize', (parseInt(br) / 2) + 'M',
    '-pix_fmt', 'yuv420p', '-g', String(Math.round(fps / 2)), '-bf', '0', '-an',
    '-movflags', 'frag_keyframe+empty_moov+default_base_moof', '-flush_packets', '1', '-f', 'mp4', 'pipe:1'];
  initSeg = null; moofBuf = null; pend = Buffer.alloc(0);
  const p = cap = spawn(process.env.GG_FFMPEG || 'ffmpeg', args);
  p.on('error', () => { console.log('SCREEN ERROR: ffmpeg not found (winget install ffmpeg)'); if (cap === p) cap = null; });
  p.stderr.on('data', d => console.log('[screen] ' + d));
  p.stdout.on('data', c => { pend = Buffer.concat([pend, c]); parseBoxes(); });
  p.on('close', () => {
    if (cap !== p) return;   // an old process that was already replaced or stopped
    if (!initSeg && enc !== 'libx264') {   // hardware encoder could not start -> fall back to CPU automatically
      console.log('SCREEN: ' + enc + ' is not available on this PC - switching to CPU (x264) automatically');
      cap = null; startScreen('libx264'); return;
    }
    stopScreen();
  });
  console.log('SCREEN sharing started (' + enc + ', ' + br + ', ' + fps + ' fps)');
}
process.stdin.on('data', d => {
  for (const line of d.toString().split(/\r?\n/)) {
    const c = line.trim(); if (!c) continue;
    if (c.startsWith('screen on')) {
      // format: screen on <encoder> <bitrate> <fps> <ffmpeg path (may contain spaces)>
      const m = c.match(/^screen on\s+(libx264|h264_nvenc|h264_qsv|h264_amf)\s+(\d{1,3}M)\s+(\d{1,3})\s+(.+)$/i);
      if (m) {
        if (cap) stopScreen();   // restart with the new settings
        process.env.GG_ENC = m[1]; process.env.GG_BR = m[2];
        process.env.GG_FPS = String(Math.min(120, Math.max(1, +m[3]))); process.env.GG_FFMPEG = m[4].trim();
      } else { const fp = c.slice(9).trim(); if (fp) process.env.GG_FFMPEG = fp; }
      startScreen();
    } else if (c === 'screen off') stopScreen();
  }
});
process.on('exit', () => { if (cap) try { cap.kill('SIGKILL'); } catch {} });

const server = http.createServer((req, res) => {
  const u = new URL(req.url, 'http://localhost');
  const ra = req.socket.remoteAddress || '';
  const ip = (/127\.0\.0\.1|::1/.test(ra) ? (req.headers['cf-connecting-ip'] || (req.headers['x-forwarded-for'] || '').split(',')[0].trim() || ra) : ra).replace('::ffff:', '');
  let dev = devices.get(ip);
  if (!dev) { dev = { ip, ua: uaName(req.headers['user-agent'] || ''), bytes: 0, last: 0 }; devices.set(ip, dev); console.log('CONNECTED ' + ip + ' (' + dev.ua + ')'); }
  dev.last = Date.now();
  let rel; try { rel = decodeURIComponent(u.pathname); } catch { return send(res, 400, 'Bad request'); }

  // Expiring share link: /s/<token>/<name>
  if (rel.startsWith('/s/')) {
    const sh = shares.get(rel.split('/')[2]);
    if (!sh || sh.exp < Date.now()) return send(res, 410, 'This share link has expired');
    return fs.stat(sh.file, (e, st) => e || st.isDirectory() ? send(res, 404, 'Not found') : serveFile(req, res, u, sh.file, st, dev));
  }

  // Login
  if (rel === '/__login' && req.method === 'POST') {
    let b = ''; req.on('data', d => { b += d; if (b.length > 2000) req.destroy(); });
    req.on('end', () => {
      if (blocked(ip)) return send(res, 429, 'Too many wrong attempts. Try again in 10 minutes.');
      const role = tryPw(ip, new URLSearchParams(b).get('pw'));
      if (!role) { res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' }); return res.end(LOGIN.replace('{MSG}', 'Wrong password')); }
      const t = crypto.randomBytes(24).toString('hex'); sessions.set(t, role);
      console.log('LOGIN ' + role + ' <- ' + ip);
      res.writeHead(302, { 'Set-Cookie': 'gg=' + t + '; HttpOnly; SameSite=Lax; Path=/; Max-Age=2592000', Location: '/' }); res.end();
    });
    return;
  }

  // Role: session cookie > ?key / header > default
  // players such as VLC send no cookie: accept ?key=, the x-gg-pw header, or HTTP Basic auth (user name ignored, password = viewer/admin password)
  let basicPw = ''; { const ah = req.headers.authorization || ''; if (/^Basic /i.test(ah)) { try { const d = Buffer.from(ah.slice(6), 'base64').toString(); basicPw = d.slice(d.indexOf(':') + 1); } catch {} } }
  let role = sessions.get(cookie(req)) || tryPw(ip, u.searchParams.get('key') || req.headers['x-gg-pw'] || basicPw);
  if (!role) role = VIEW ? null : (ADMIN ? 'viewer' : 'admin');
  if (!role) {
    if ((req.headers.accept || '').includes('text/html')) { res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' }); return res.end(LOGIN.replace('{MSG}', blocked(ip) ? 'Too many attempts. Wait 10 minutes.' : '')); }
    res.writeHead(401, { 'Content-Type': 'text/plain', 'WWW-Authenticate': 'Basic realm="GGShare"' }); return res.end('Login required');
  }
  const canAdmin = role === 'admin' && !RO;
  const ctl = role === 'admin' && !!ADMIN;   // server controls need a real admin password (never open to everyone)

  // ---- Server controls (admin only) ----
  if (rel.startsWith('/__admin/')) {
    if (!ctl) return send(res, 403, 'Server controls need the admin password');
    const act = rel.slice(9);
    const out = (code, o) => { res.writeHead(code, { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' }); res.end(JSON.stringify(o)); };
    if (act === 'info' && req.method === 'GET') {
      const now = Date.now(), list = [];
      for (const d of devices.values()) list.push({ ip: d.ip, ua: d.ua, age: Math.round((now - d.last) / 1000), bytes: d.bytes });
      return out(200, { uptime: Math.round((now - started) / 1000), up, down, ro: RO, managed: MANAGED, screen: !!(cap && initSeg), fps: curFps, br: curBr,
        viewers: viewers.size, sessions: sessions.size, shares: shares.size, node: process.version, devices: list,
        folders: mounts.map(m => ({ name: m.name, dir: m.dir, ok: fs.existsSync(m.dir) })) });
    }
    if (req.method !== 'POST' || req.headers['x-requested-with'] !== 'GGShare') return out(400, { ok: false, msg: 'Bad request' });
    console.log('ADMIN ' + act + ' <- ' + ip);
    switch (act) {
      case 'restart':
        if (!MANAGED) return out(400, { ok: false, msg: 'Restart needs the GGShare app (this server was started by hand).' });
        out(200, { ok: true, msg: 'Restarting...' }); setTimeout(() => { console.log('@@RESTART'); process.exit(0); }, 600); return;
      case 'shutdown':
        out(200, { ok: true, msg: 'Shutting down...' }); setTimeout(() => { console.log('@@SHUTDOWN'); process.exit(0); }, 600); return;
      case 'link-restart':
        if (!MANAGED) return out(400, { ok: false, msg: 'Needs the GGShare app.' });
        console.log('@@TUNNEL'); return out(200, { ok: true, msg: 'Restarting the internet link. The page may disconnect for a few seconds.' });
      case 'screen-start':
        if (cap) return out(200, { ok: true, msg: 'Screen sharing is already running.' });
        startScreen(); return out(200, { ok: true, msg: 'Starting screen share...' });
      case 'screen-stop':
        stopScreen(); return out(200, { ok: true, msg: 'Screen sharing stopped.' });
      case 'ro':
        RO = !RO; return out(200, { ok: true, ro: RO, msg: RO ? 'Read-only mode is ON (until the server restarts).' : 'Read-only mode is OFF (until the server restarts).' });
      case 'logout-all': {
        const mine = cookie(req); let n = 0;
        for (const k of Array.from(sessions.keys())) if (k !== mine) { sessions.delete(k); n++; }
        return out(200, { ok: true, msg: 'Logged out ' + n + ' other session' + (n === 1 ? '' : 's') + '.' });
      }
      case 'revoke-shares': { const n = shares.size; shares.clear(); return out(200, { ok: true, msg: 'Revoked ' + n + ' share link' + (n === 1 ? '' : 's') + '.' }); }
      case 'empty-trash': {
        let n = 0;
        for (const m of mounts) { try { for (const f of fs.readdirSync(m.trash)) { fs.rmSync(path.join(m.trash, f), { recursive: true, force: true }); n++; } } catch {} }
        return out(200, { ok: true, msg: 'Permanently deleted ' + n + ' item' + (n === 1 ? '' : 's') + ' from the recycle bin.' });
      }
      default: return out(404, { ok: false, msg: 'Unknown action' });
    }
  }

  if (rel === '/__screen') { res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store' }); return res.end(SCREEN_HTML); }
  if (rel === '/__screen/status') { res.writeHead(200, { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' }); return res.end(JSON.stringify({ live: !!(cap && initSeg), fps: curFps, br: curBr, viewers: viewers.size })); }
  if (rel === '/__screen/stream') {
    if (!cap || !initSeg) return send(res, 503, 'Screen share is not running');
    res.writeHead(200, { 'Content-Type': 'video/mp4', 'Cache-Control': 'no-store' });
    res.write(initSeg); viewers.add(res); res.on('close', () => viewers.delete(res)); return;
  }

  const rr = resolveRel(rel);
  if (rr.bad) return send(res, 403, 'Forbidden');
  if (rr.missing) return send(res, 404, 'Not found');
  if (rr.virtual) {   // top level when several folders are shared: list them
    if (req.method !== 'GET' && req.method !== 'HEAD') return send(res, 403, 'Open a folder first');
    if (u.searchParams.has('zip')) return send(res, 400, 'Open a folder first');
    if (u.searchParams.has('json')) return json(res, { path: u.pathname, items: mounts.map(m => ({ name: m.name, isDir: true, size: 0 })), role, ro: RO, ctl: role === 'admin' && !!ADMIN, canAdmin: false, canLogin: !!ADMIN && role !== 'admin', screen: !!(cap && initSeg), sfps: curFps, sbr: curBr });
    return fs.readFile(uiFile, (e, data) => { if (e) return send(res, 500, 'index.html missing'); res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store' }); res.end(data); });
  }
  const full = rr.full, mount = rr.m, root = mount.dir, rootSep = mount.sep, TRASH = mount.trash;
  if (full === TRASH || full.startsWith(TRASH + path.sep)) return send(res, 404, 'Not found');

  // ---------- Admin actions ----------
  if (req.method === 'PUT' || req.method === 'DELETE' || req.method === 'POST') {
    if (!canAdmin) return send(res, 403, RO ? 'Server is in read-only mode' : 'Admin password required');
    if (req.method === 'DELETE') {
      if (full === root) return send(res, 403, 'Cannot delete the shared root');
      try { fs.mkdirSync(TRASH, { recursive: true }); } catch {}
      return fs.rename(full, path.join(TRASH, Date.now() + '_' + path.basename(full)), er => {
        if (er) return send(res, 409, 'Could not move to recycle bin');
        console.log('DELETED ' + path.basename(full) + ' (recycle bin, 30 days) <- ' + ip); send(res, 200, 'Moved to recycle bin');
      });
    }
    if (req.method === 'POST') {
      if (u.searchParams.has('mkdir')) return fs.mkdir(path.join(full, clean(u.searchParams.get('mkdir'))), er => er ? send(res, 409, 'Could not create folder') : send(res, 200, 'Created'));
      if (u.searchParams.has('rename')) {
        const to = path.join(path.dirname(full), clean(u.searchParams.get('rename')));
        if (full === root || fs.existsSync(to)) return send(res, 409, 'Name already exists');
        return fs.rename(full, to, er => er ? send(res, 409, 'Rename failed') : send(res, 200, 'Renamed'));
      }
      if (u.searchParams.has('share')) {
        const t = crypto.randomBytes(12).toString('hex'); shares.set(t, { file: full, exp: Date.now() + 864e5 });
        return json(res, { url: '/s/' + t + '/' + encodeURIComponent(path.basename(full)) });
      }
      return send(res, 400, 'Bad request');
    }
    const name = clean(path.basename(full)), dir = path.dirname(full);
    if (dir !== root && !dir.startsWith(rootSep)) return send(res, 403, 'Forbidden');
    return fs.stat(dir, (e, ds) => {
      if (e || !ds.isDirectory()) return send(res, 404, 'Folder not found');
      const ext = path.extname(name), base = path.basename(name, ext);
      let target = path.join(dir, name), n = 0;
      while (fs.existsSync(target)) target = path.join(dir, base + ' (' + (++n) + ')' + ext);
      const part = target + '.part', ws = fs.createWriteStream(part);
      const bail = () => { ws.destroy(); fs.unlink(part, () => {}); };
      req.on('aborted', bail); req.on('data', d => { up += d.length; dev.bytes += d.length; });
      ws.on('error', () => { bail(); if (!res.headersSent) send(res, 500, 'Write error'); });
      ws.on('finish', () => fs.rename(part, target, er => { if (er) return send(res, 500, 'Save failed'); console.log('UPLOAD ' + path.basename(target) + ' <- ' + ip); json(res, { name: path.basename(target) }); }));
      req.pipe(ws);
    });
  }

  fs.stat(full, (err, st) => {
    if (err) return send(res, 404, 'Not found');
    if (st.isDirectory()) {
      if (!u.pathname.endsWith('/')) { res.writeHead(301, { Location: u.pathname + '/' }); return res.end(); }
      if (u.searchParams.has('json')) {
        return fs.readdir(full, { withFileTypes: true }, (e, entries) => {
          if (e) return send(res, 500, 'Cannot read folder');
          const items = [];
          for (const d of entries) { if (d.name === '.trash') continue; try { const s = fs.statSync(path.join(full, d.name)); items.push({ name: d.name, isDir: s.isDirectory(), size: s.isDirectory() ? 0 : s.size }); } catch {} }
          json(res, { path: u.pathname, items, role, ro: RO, ctl: role === 'admin' && !!ADMIN, canAdmin, canLogin: !!ADMIN && role !== 'admin', screen: !!(cap && initSeg), sfps: curFps, sbr: curBr });
        });
      }
      if (u.searchParams.has('zip')) {
        res.writeHead(200, { 'Content-Type': 'application/zip', 'Content-Disposition': 'attachment; filename="' + encodeURIComponent(path.basename(full) || 'files') + '.zip"' });
        const t = spawn(process.env.GG_TAR || 'tar', ['--format', 'zip', '-cf', '-', '-C', path.dirname(full), path.basename(full)]);
        t.on('error', () => res.destroy()); t.stdout.on('data', d => { down += d.length; dev.bytes += d.length; }); t.stdout.pipe(res); res.on('close', () => t.kill());
        return;
      }
      return fs.readFile(uiFile, (e, data) => { if (e) return send(res, 500, 'index.html missing'); res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store' }); res.end(data); });
    }
    const wantsPage = (req.headers.accept || '').includes('text/html');
    if (u.searchParams.has('play') && wantsPage) { res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store' }); return res.end(playerHtml(path.basename(full), u.pathname)); }
    if (u.searchParams.has('transcode')) return transcode(full, req, res);
    serveFile(req, res, u, full, st, dev);
  });
});
server.keepAliveTimeout = 120000; server.headersTimeout = 125000; server.requestTimeout = 0; server.timeout = 0;
server.listen(port, '0.0.0.0', () => console.log('Serving ' + mounts.map(m => m.dir).join(', ') + ' on port ' + port));
setInterval(() => {
  const now = Date.now(), list = [];
  for (const [k, d] of devices) { if (now - d.last > 120000) devices.delete(k); else list.push({ ip: d.ip, ua: d.ua, age: Math.round((now - d.last) / 1000), bytes: d.bytes }); }
  console.log('@@STATS ' + JSON.stringify({ up, down, uptime: Math.round((now - started) / 1000), devices: list }));
}, 2000);
