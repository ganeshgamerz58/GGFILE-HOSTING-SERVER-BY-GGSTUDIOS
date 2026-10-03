# GGSTUDIOS File Share

A self-hosted file server and live screen sharer for Windows. Share one or many folders from your PC through a clean web page, stream videos straight to VLC or your browser, put it all on the internet with a **fixed link**, and let it run silently in the system tray from the moment Windows starts.

No cloud account, no upload limits, no subscription. Your files stay on your PC.

---

## Features

**File sharing**
- Share **multiple folders at once**; each one appears as a top-level folder on the website
- Browse, search, download, and download whole folders as a zip
- Upload, create folders, rename and delete (admin only), with a **recycle bin** (kept for 30 days)
- **Expiring share links** (24 hours) that work without a password
- Video and audio streaming with seeking (HTTP range requests), works in browsers and **VLC**
- Mobile friendly dark interface

**Live screen sharing**
- Captures your full desktop (with the mouse) at up to **120 FPS** and streams H.264 to any modern browser
- Encoders: CPU (x264), NVIDIA NVENC, Intel QuickSync, AMD AMF. Falls back to CPU automatically if a GPU encoder is not available
- Choose bitrate (8 to 40 Mbps) and FPS (15 to 120)
- Own **Live Screen** tab on the website

**Internet access (no router setup)**
- **Random link:** free Cloudflare quick tunnel
- **Fixed / custom / short link:** Cloudflare named tunnel on your own domain
- **Fixed link, no domain needed:** ngrok static domain

**Runs by itself**
- Hidden in the **system tray only** (no taskbar button)
- **Starts with Windows**, starts the server and connects your fixed link automatically
- Restarts itself if the server crashes or the link drops
- Real **installer wizard** with uninstaller and an autostart option
- Passwords and tokens are stored encrypted with Windows DPAPI

**Web admin panel** (admin login)
- Restart server, restart internet link, shut down server
- Start / stop screen share, toggle read-only mode
- Log out other devices, revoke share links, empty the recycle bin
- Live stats: uptime, connected devices, data sent and received

**Security**
- Separate **viewer** and **admin** passwords
- Blocks an IP address for 10 minutes after repeated wrong passwords
- Read-only mode, path traversal protection, server controls disabled unless an admin password is set
- It will not go online automatically unless a viewer password is set

---

## Requirements

| What | Needed for |
|------|-----------|
| Windows 10 or 11 | the app |
| [Node.js](https://nodejs.org) (LTS) | the server (required) |
| [ffmpeg](https://ffmpeg.org) | live screen sharing (optional) |
| [cloudflared](https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/downloads/) or [ngrok](https://ngrok.com) 3.20+ | internet link (optional) |

The installer can install Node.js, ffmpeg and ngrok for you with `winget`.

---

## Supported platforms

| Host device | Files + web page | Live screen share | Auto-start |
|-------------|:---:|:---:|:---:|
| Windows 10 / 11 | yes | yes | yes (tray app + installer) |
| Linux / Raspberry Pi | yes | no | yes (systemd) |
| macOS | yes | no | yes (launchd) |
| Android (Termux) | yes | no | yes (Termux:Boot) |
| Docker | yes | no | yes |

Any device with a browser (or VLC) can open the shared files.

**Full step-by-step instructions for every platform: [SETUP.md](SETUP.md)**

---

## Quick start (Windows)

1. Download or clone this repository.
2. Double-click **`Build.bat`**. It uses the C# compiler that is already part of Windows (no Visual Studio needed) and creates:
   - `GGShare.exe` (the app)
   - `GGShare-Setup.exe` (the installer, which contains the app)
3. Run **`GGShare-Setup.exe`**, follow the wizard and tick **Start automatically when Windows starts**.
4. Open GGSTUDIOS File Share, click **Add** (or drag folders onto the list), set a **Viewer password** and an **Admin password**, then click **Start Server**.

Open the **This PC** or **Wi-Fi / LAN** link in any browser on your network.

> Windows SmartScreen may warn about an unsigned app you built yourself. Click **More info** and **Run anyway**.

### Put it online with a fixed link

Click the gear button next to **Internet** and pick a mode:

- **Cloudflare (your own domain):** in Cloudflare Zero Trust create a tunnel, copy its token, add a Public Hostname pointing to `http://localhost:<your port>`, then paste the token and hostname.
- **ngrok (free):** claim your free static domain and copy your authtoken from the ngrok dashboard, then paste both.

Click **Go Online**. Tick **Start with Windows** and **Auto-start server + go online** and it will do this on every boot, hidden in the tray.

---

## Playing videos in VLC

Use the direct file link: `http://<host>:<port>/<folder>/<file>`.

If you set a viewer password, VLC will ask for credentials. Type **any user name** and the **viewer password**. You can also use a link with the password in it (`...?key=PASSWORD`) or a **24 hour share link** from the website, which needs no password.

---

## Run the server without the app

The server is a single Node.js file, so it also runs on its own (Windows, Linux or macOS). Screen sharing is Windows only.

```bash
# share one folder
GG_ROOT=/path/to/folder node server.js

# share several, with passwords, on port 9000
GG_ROOTS='["/data/movies","/data/docs"]' GG_PORT=9000 GG_VIEW=viewpass GG_ADMIN=adminpass node server.js
```

On Windows Command Prompt use `set GG_ROOT=D:\Share` then `node server.js`.

| Variable | Meaning | Default |
|----------|---------|---------|
| `GG_ROOT` | folder to share | none |
| `GG_ROOTS` | JSON array of folders to share | none |
| `GG_PORT` | port | `8080` |
| `GG_VIEW` | viewer password | none |
| `GG_ADMIN` | admin password (needed for uploads, deletes and server controls) | none |
| `GG_RO` | `1` for read-only | off |
| `GG_UI` | path to `index.html` | next to `server.js` |
| `GG_ENC` `GG_BR` `GG_FPS` `GG_FFMPEG` | screen share encoder, bitrate (e.g. `15M`), FPS, ffmpeg path | `libx264`, `15M`, `60` |

With no passwords set, anyone who can reach the server can use it. Always set passwords before putting it on the internet.

---

## Project layout

| File | Purpose |
|------|---------|
| `server.js` | the web server (files, auth, screen stream, admin API) |
| `index.html` | the website (files, live screen, server controls) |
| `GGShare.cs` | the Windows tray app |
| `Setup.cs`, `Setup.manifest` | the installer and uninstaller |
| `Build.bat` | builds `GGShare.exe` and `GGShare-Setup.exe` |
| `app.ico` | icon |

The app is plain C# for the .NET Framework 4 compiler that ships with Windows, so there is nothing extra to install to build it.

---

## Troubleshooting

- **Screen share says ffmpeg is missing:** run `winget install ffmpeg`, then click the button again.
- **GPU encoder error:** pick CPU (x264). The app also switches to CPU automatically.
- **ngrok says the version is too old:** run `ngrok update`.
- **Other devices on Wi-Fi cannot connect:** allow Node.js through Windows Firewall on private networks.
- **VLC cannot open the link:** include the folder name in the path and enter the viewer password when asked.

---

## Security notes

- The local web server speaks plain HTTP. The Cloudflare and ngrok tunnels add HTTPS for internet access.
- A fixed public link is easy to find, so use strong passwords.
- Only share files you have the right to share.

## License

MIT, see [LICENSE](LICENSE).
