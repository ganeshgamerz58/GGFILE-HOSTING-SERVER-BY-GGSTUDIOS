# Setup guide

Step-by-step instructions for every platform. Pick the section for the device that will **host** the files.

| Host device | Files + web page | Live screen share | Tray app + auto-start | Section |
|-------------|:---:|:---:|:---:|---------|
| Windows 10 / 11 | yes | yes | yes | [1. Windows](#1-windows-full-features) |
| Linux / Raspberry Pi | yes | no | yes (systemd) | [2. Linux](#2-linux-and-raspberry-pi) |
| macOS | yes | no | yes (launchd) | [3. macOS](#3-macos) |
| Android (Termux) | yes | no | yes (Termux:Boot) | [4. Android](#4-android-termux) |
| Docker (any OS) | yes | no | yes | [5. Docker](#5-docker) |

Anything with a web browser can **open** the shared files: phones, tablets, TVs, other PCs. See [6. Using it from any device](#6-using-it-from-any-device).

Every platform uses the same two files for the server: `server.js` and `index.html`. The Windows app (`GGShare.exe`) is a friendly wrapper around them.

---

## 1. Windows (full features)

### Step 1: Install Node.js

1. Open **Command Prompt** (press the Windows key, type `cmd`, press Enter).
2. Run:
   ```
   winget install OpenJS.NodeJS.LTS
   ```
   Or download the LTS installer from https://nodejs.org and run it.
3. Close and reopen Command Prompt, then check it worked:
   ```
   node --version
   ```
   You should see a version number such as `v20.x`.

The installer in step 3 can also do this for you, so you can skip it if you like.

### Step 2: Get the project and build it

1. On the GitHub page click **Code**, then **Download ZIP**. (Or run `git clone <repo-url>`.)
2. Right-click the ZIP, choose **Extract All**.
3. Open the extracted folder and double-click **`Build.bat`**.
4. Wait for "Done". You now have two new files: `GGShare.exe` and **`GGShare-Setup.exe`**.

No Visual Studio is needed. `Build.bat` uses the C# compiler that is already part of Windows. If it prints an error, see [Troubleshooting](#troubleshooting).

### Step 3: Run the installer

1. Double-click **`GGShare-Setup.exe`** and click **Yes** on the Windows permission prompt.
   (If SmartScreen says "Windows protected your PC": click **More info**, then **Run anyway**. This is normal for apps you build yourself.)
2. **Welcome** page: click Next.
3. **Install location** page: keep the default (`C:\Program Files\GGSTUDIOS\GGShare`) and click Next.
4. **Options** page:
   - Tick **Start automatically when Windows starts (hidden, tray only)**. This is what makes it run by itself.
   - Keep the desktop and Start menu shortcuts ticked if you want them.
   - Under **Required components**, leave Node.js ticked if you do not have it. Tick ffmpeg if you want live screen sharing. Tick ngrok if you will use the free fixed link.
5. Click **Install**, wait for it to finish, then click **Finish** (leave "Open now" ticked).

### Step 4: First-time configuration

The app window opens. Fill it in once:

1. **Shared folders:** click **Add** and pick a folder. Repeat for more folders. You can also drag folders from Explorer onto the list. Select folders and click **Remove** to remove them.
2. **Port:** keep `8080` unless something else uses it.
3. **Viewer password:** the password people need to open your files. **Set one.**
4. **Admin password:** needed for uploading, deleting, renaming and the server controls. **Set one.**
5. Tick **Start with Windows** and **Auto-start server + go online when the app opens**.
6. Click **START SERVER**.

The **This PC** link opens in your browser. The **Wi-Fi / LAN** link works for other devices on the same Wi-Fi.

> If Windows asks to allow Node.js through the firewall, tick **Private networks** and click **Allow access**. If you missed the prompt, open Command Prompt **as administrator** and run:
> `netsh advfirewall firewall add rule name="GGShare" dir=in action=allow protocol=TCP localport=8080 profile=private`

### Step 5: Put it on the internet with a fixed link

Click the **gear button** next to **Internet**. Choose one option.

**Option A: ngrok (free, no domain needed)**

1. Open Command Prompt and run `winget install ngrok.ngrok`. Then run `ngrok update` to make sure it is version 3.20 or newer.
2. Create a free account at https://dashboard.ngrok.com.
3. In the dashboard open **Domains**. You get one free domain, for example `your-name-here.ngrok-free.dev`. Copy it.
4. Open **Your Authtoken** in the left menu and copy the token. Keep it private.
5. In GGShare, in the gear window, choose **Fixed link (ngrok)**.
6. Paste the domain (no `https://`) and the authtoken. Click **SAVE**.
7. Click **GO ONLINE**. After a few seconds the **Internet** box shows your link.

The link never changes. The free plan shows visitors a one-time "Visit Site" page and has a monthly data limit, so it is best for files, not heavy live video.

**Option B: Cloudflare (short custom link on your own domain)**

You need a domain name (a few dollars a year) added to a free Cloudflare account.

1. In Cloudflare open **Zero Trust**, then **Networks**, then **Tunnels**, and click **Create a tunnel**. Choose **Cloudflared** and give it a name. (Cloudflare may ask you to add a payment method to activate the free Zero Trust plan.)
2. Copy the **token**. It is the very long text shown in the install command.
3. Open the **Public Hostname** tab and click **Add**:
   - Subdomain and domain: for example `go` and `yourdomain.com`
   - Service type: **HTTP**
   - URL: `localhost:8080` (use your own port if you changed it)
4. In GGShare click the gear, choose **Fixed / custom / short link (Cloudflare)**, paste the token and your link (`go.yourdomain.com`), and click **SAVE**.
5. Click **GO ONLINE**.

**Option C: Random link (free, changes every time)**

Choose **Random link**, then **GO ONLINE**. Install `cloudflared` first with `winget install Cloudflare.cloudflared`.

### Step 6: Make it run by itself

Because you ticked both startup boxes, the app now starts when you sign in to Windows, hidden in the tray, starts the server and connects your link.

- Open it any time by **double-clicking the tray icon** (bottom right, it may be under the `^` arrow). Right-click for **Open**, **Start / stop server**, **Go online / offline**, **Copy internet link** and **Exit**.
- It restarts itself if the server crashes or the link drops.
- It will **not** go online automatically unless a viewer password is set. This protects your files.
- It starts after you sign in, not before. To start with no sign-in, set up Windows auto-login.

### Step 7: Live screen sharing (optional)

1. Install ffmpeg: `winget install ffmpeg` (or tick it in the installer).
2. In the app choose an **encoder**: NVIDIA, Intel or AMD if you have that GPU, otherwise CPU (x264). It switches to CPU by itself if the GPU encoder fails.
3. Choose a **bitrate** (15 Mbps is good for 1080p) and **FPS** (30 or 60).
4. Click **START SCREEN SHARE**.

Viewers open your link and click the **Live Screen** tab. Every viewer receives a full copy of the stream, so keep the bitrate low when sharing over the internet.

### Step 8: The web admin panel

Log in on the website with the **admin password**. A **Server** tab appears with: restart server, restart internet link, shut down server, start/stop screen share, read-only mode, log out other devices, revoke share links, empty recycle bin, and live stats. It only appears if an admin password is set.

### Update or uninstall

- **Update:** download the new version, run `Build.bat` again, then run the new `GGShare-Setup.exe`. It replaces the old files and keeps your settings.
- **Uninstall:** Windows **Settings**, then **Apps**, then **GGSTUDIOS File Share**, then **Uninstall** (or the Start menu entry). It asks whether to also delete your saved settings.

### Windows without the installer

Run `GGShare.exe` straight from the build folder, or run only the server:

```
set GG_ROOT=D:\Share
set GG_VIEW=viewpass
set GG_ADMIN=adminpass
node server.js
```

---

## 2. Linux and Raspberry Pi

Screen sharing and the tray app are Windows only. Everything else works.

### Step 1: Install Node.js

Use Node.js 18 or newer.

```bash
# Debian / Ubuntu / Raspberry Pi OS
sudo apt update && sudo apt install -y nodejs npm
node --version

# if the version is too old, use NodeSource:
curl -fsSL https://deb.nodesource.com/setup_lts.x | sudo -E bash -
sudo apt install -y nodejs
```

Fedora: `sudo dnf install nodejs`. Arch: `sudo pacman -S nodejs`.

### Step 2: Get the files

```bash
git clone <repo-url> ggshare
cd ggshare
```

Only `server.js` and `index.html` are needed.

### Step 3: Run it once to test

```bash
GG_ROOT=/path/to/share GG_VIEW=viewpass GG_ADMIN=adminpass node server.js
```

Open `http://<this-machine-ip>:8080` from another device. Find the IP with `hostname -I`. Press Ctrl+C to stop.

Several folders: `GG_ROOTS='["/srv/movies","/srv/docs"]'`.

If a firewall is on: `sudo ufw allow 8080/tcp`.

### Step 4: Start it at boot (systemd)

```bash
sudo mkdir -p /opt/ggshare
sudo cp server.js index.html /opt/ggshare/
sudo nano /etc/ggshare.env
```

Put this in `/etc/ggshare.env` (edit the values):

```
GG_ROOT=/srv/share
GG_PORT=8080
GG_VIEW=viewpass
GG_ADMIN=adminpass
```

Then protect it and create the service:

```bash
sudo chmod 600 /etc/ggshare.env
sudo nano /etc/systemd/system/ggshare.service
```

```ini
[Unit]
Description=GGShare file server
After=network-online.target
Wants=network-online.target

[Service]
EnvironmentFile=/etc/ggshare.env
WorkingDirectory=/opt/ggshare
ExecStart=/usr/bin/node /opt/ggshare/server.js
Restart=always
RestartSec=5
User=YOUR_USER

[Install]
WantedBy=multi-user.target
```

Use the output of `which node` if it is not `/usr/bin/node`, and set `User=` to an account that can read (and write, for uploads) the shared folder.

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now ggshare
sudo systemctl status ggshare
journalctl -u ggshare -f          # live log
```

Update later: copy the new `server.js` and `index.html` into `/opt/ggshare/` and run `sudo systemctl restart ggshare`.

### Step 5: Fixed internet link

**Cloudflare** (follow [Windows Option B](#step-5-put-it-on-the-internet-with-a-fixed-link) steps 1 to 3 in the Cloudflare dashboard, pointing the hostname to `http://localhost:8080`), then on the Linux machine:

```bash
# install cloudflared: https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/downloads/
sudo cloudflared service install YOUR_TUNNEL_TOKEN
```

This installs a service that starts at boot.

**ngrok:** install it from https://ngrok.com/download, run `ngrok config add-authtoken YOUR_TOKEN`, then:

```bash
ngrok http --url=your-name.ngrok-free.dev 8080
```

To keep it running after you close the terminal, run it inside `tmux` or create a second systemd service for it.

**Quick random link:** `cloudflared tunnel --url http://localhost:8080`

---

## 3. macOS

Screen sharing and the tray app are Windows only.

### Step 1: Install Node.js

```bash
brew install node        # with Homebrew
# or download the LTS installer from https://nodejs.org
node --version
```

### Step 2: Get the files and test

```bash
git clone <repo-url> ggshare
cd ggshare
GG_ROOT=~/Share GG_VIEW=viewpass GG_ADMIN=adminpass node server.js
```

When macOS asks **Do you want the application "node" to accept incoming network connections?** click **Allow**. Open `http://<your-mac-ip>:8080` from another device (find the IP in System Settings, then Wi-Fi, then Details).

### Step 3: Start at login (launchd)

Find the paths you need: run `which node` (for example `/opt/homebrew/bin/node`) and `pwd` inside the project folder.

Create `~/Library/LaunchAgents/com.ggshare.plist`:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key><string>com.ggshare</string>
  <key>ProgramArguments</key>
  <array>
    <string>/opt/homebrew/bin/node</string>
    <string>/Users/YOU/ggshare/server.js</string>
  </array>
  <key>EnvironmentVariables</key>
  <dict>
    <key>GG_ROOT</key><string>/Users/YOU/Share</string>
    <key>GG_VIEW</key><string>viewpass</string>
    <key>GG_ADMIN</key><string>adminpass</string>
  </dict>
  <key>RunAtLoad</key><true/>
  <key>KeepAlive</key><true/>
</dict>
</plist>
```

Then:

```bash
launchctl load ~/Library/LaunchAgents/com.ggshare.plist
```

Stop it with `launchctl unload ~/Library/LaunchAgents/com.ggshare.plist`.

### Step 4: Fixed internet link

Same as Linux step 5. Install the tools with `brew install cloudflared` or `brew install ngrok`.

---

## 4. Android (Termux)

Host files straight from your phone. Screen sharing and the tray app are not available.

### Step 1: Install Termux

1. Install **Termux** from **F-Droid** (https://f-droid.org) or the Termux GitHub releases page. **Do not use the Play Store version**; it is outdated.
2. Allow "install unknown apps" when asked.

### Step 2: Install Node.js and get storage access

Open Termux and run these one at a time:

```bash
pkg update && pkg upgrade -y
pkg install nodejs -y
termux-setup-storage
```

Tap **Allow** on the permission pop-up.

### Step 3: Put the files on the phone

Copy `server.js` and `index.html` to the phone (USB cable, Google Drive, or send them to yourself), into a folder such as `Download/GGShare`. Then in Termux:

```bash
mkdir -p ~/ggshare
cp /sdcard/Download/GGShare/server.js /sdcard/Download/GGShare/index.html ~/ggshare/
cd ~/ggshare
```

### Step 4: Start the server

```bash
GG_ROOT=/sdcard/Download GG_PORT=8080 GG_VIEW=viewpass GG_ADMIN=adminpass node server.js
```

To share more than one folder: `GG_ROOTS='["/sdcard/Download","/sdcard/DCIM"]'`.

Find your phone's IP in **Settings, then Wi-Fi, then your network**. On another device on the same Wi-Fi open `http://PHONE-IP:8080`. Press Ctrl+C (the CTRL key on the Termux key row, then C) to stop.

### Step 5: Keep it running

1. Run `termux-wake-lock` (or tap **Acquire wakelock** in the Termux notification).
2. Phone **Settings, then Apps, then Termux, then Battery**: set it to **Unrestricted**.
3. Do not swipe Termux away from recent apps.

### Step 6: Start when the phone boots

1. Install **Termux:Boot** from F-Droid and open it once.
2. In Termux run:
   ```bash
   mkdir -p ~/.termux/boot
   nano ~/.termux/boot/ggshare.sh
   ```
3. Paste, then save (Ctrl+O, Enter, Ctrl+X):
   ```bash
   #!/data/data/com.termux/files/usr/bin/sh
   termux-wake-lock
   cd ~/ggshare
   GG_ROOT=/sdcard/Download GG_PORT=8080 GG_VIEW=viewpass GG_ADMIN=adminpass node server.js
   ```
4. Run `chmod +x ~/.termux/boot/ggshare.sh`.

### Step 7: Internet link (optional)

```bash
pkg install cloudflared -y
cloudflared tunnel --url http://localhost:8080
```

It prints a random `trycloudflare.com` link. For a fixed link, create a Cloudflare tunnel as in the Windows guide and run `cloudflared tunnel run --token YOUR_TOKEN`. If the package is not found, update with `pkg update` or stay on Wi-Fi only.

---

## 5. Docker

Works on any machine with Docker (Linux, Windows, macOS, NAS devices).

```bash
git clone <repo-url> ggshare
cd ggshare

docker run -d --name ggshare --restart unless-stopped \
  -p 8080:8080 \
  -v "$(pwd)":/app:ro \
  -v /path/to/share:/share \
  -e GG_ROOT=/share \
  -e GG_VIEW=viewpass \
  -e GG_ADMIN=adminpass \
  node:lts-alpine node /app/server.js
```

- On Windows PowerShell use `${PWD}` instead of `$(pwd)`, and put the command on one line (no backslashes).
- Several folders: mount each one (`-v /a:/share/a -v /b:/share/b`) and set `-e GG_ROOTS='["/share/a","/share/b"]'`.
- View the log: `docker logs -f ggshare`. Update: replace the files, then `docker restart ggshare`.
- For an internet link, run `cloudflared` or `ngrok` on the Docker host and point it at `http://localhost:8080`.

---

## 6. Using it from any device

| Device | How to open |
|--------|-------------|
| Any phone, tablet or PC | Open the link in the browser. Enter the viewer password. |
| Windows, Mac, Linux, Android with VLC | **Media, Open Network Stream**, paste `http://HOST:8080/Folder/file.mkv`. When VLC asks for a login, type **any user name** and the viewer password. |
| Smart TV or media player | Use its VLC, Kodi or browser app with the same direct link. If it cannot enter a password, use a **24 hour share link** from the website (the link icon next to a file, admin login needed). |
| iPhone / iPad | Browser or the VLC app. Hosting from iOS is not supported. |

Link types:
- **Same Wi-Fi:** `http://HOST-IP:8080`
- **Anywhere:** your fixed internet link (`https://...`)

---

## Troubleshooting

| Problem | Fix |
|---------|-----|
| `Build.bat` shows an error | Copy the error lines and open a GitHub issue. Make sure you extracted the ZIP and did not run it from inside the ZIP. |
| App says Node.js is missing | Install it (`winget install OpenJS.NodeJS.LTS`) and click Start Server again. |
| Other devices cannot open the LAN link | Allow Node.js through the firewall on private networks (see Windows step 4) and make sure both devices are on the same Wi-Fi. |
| Screen share says ffmpeg is missing | `winget install ffmpeg`, then click the button again. |
| GPU encoder error (AMD/NVIDIA/Intel) | Choose CPU (x264). The app also falls back to CPU automatically. |
| ngrok: "agent version too old" | Run `ngrok update` (or `winget upgrade ngrok.ngrok`). It must be 3.20 or newer. |
| ngrok link shows "Not found" | The server is not running, or the shared folder does not exist. Start the server and check the folder path. |
| It did not go online at startup | A viewer password must be set. Set one, then click GO ONLINE once. |
| VLC cannot open the link | Include the folder name in the path, and enter the viewer password when asked. |
| Forgot the passwords | Windows: open the app and set new ones. Linux/Docker: change the environment values and restart. |

---

## Safety checklist before going online

1. Set a strong **viewer password** and a different **admin password**.
2. Share only the folders you mean to share (not a whole drive with personal files).
3. Keep your ngrok authtoken and Cloudflare token private. Never post them or commit them to GitHub.
4. Use **read-only mode** if people only need to download.
5. Only share files you have the right to share.
