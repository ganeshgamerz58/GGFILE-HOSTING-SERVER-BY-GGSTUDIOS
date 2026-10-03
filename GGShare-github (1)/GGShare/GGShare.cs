using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

class Program
{
    [STAThread] static void Main(string[] a)
    {
        bool created;
        var mtx = new System.Threading.Mutex(true, "Local\\GGShare_SingleInstance", out created);
        var evt = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, "Local\\GGShare_Show");
        if (!created) { if (Array.IndexOf(a, "--tray") < 0) { try { evt.Set(); } catch { } } return; }
        Application.EnableVisualStyles(); Application.Run(new MainForm(a, evt)); GC.KeepAlive(mtx);
    }
}

class MainForm : Form
{
    static Color BG = Color.FromArgb(10, 14, 26), SURF = Color.FromArgb(20, 24, 36), LINE = Color.FromArgb(31, 37, 53), TXT = Color.FromArgb(232, 237, 245), DIM = Color.FromArgb(138, 146, 168), ACC = Color.FromArgb(0, 217, 255);
    TextBox txtView = new TextBox(), txtAdmin = new TextBox(), txtLog = new TextBox(); ListBox lstFolders = new ListBox();
    NumericUpDown numPort = new NumericUpDown();
    CheckBox chkRo = new CheckBox(), chkBoot = new CheckBox(), chkAuto = new CheckBox();
    Button btnStart, btnTunnel, btnAddF, btnRemF, btnScreen; ComboBox cmbEnc = new ComboBox(), cmbBr = new ComboBox(), cmbFps = new ComboBox(); TextBox urlScr = new TextBox(); bool screenOn = false;
    TextBox urlLocal = new TextBox(), urlNet = new TextBox(), urlOnline = new TextBox();
    Label[] tileVal = new Label[4];
    double[] shown = new double[4], target = new double[4];
    ListView lv = new ListView();
    Process server, tunnel;
    NotifyIcon tray = new NotifyIcon();
    Timer anim = new Timer();
    double phase = 0; bool running = false; bool reallyExit = false;
    bool webRestart = false, loading = false, startHidden = false, quiet = false, wantServer = false, wantOnline = false; int tunRetries = 0, trayTick = 0;
    Timer bootT, restartT, tunT;
    string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GGShare");
    JavaScriptSerializer js = new JavaScriptSerializer();

    public MainForm(string[] args, System.Threading.EventWaitHandle showEvt)
    {
        startHidden = Array.IndexOf(args, "--tray") >= 0; ShowInTaskbar = false;
        Text = "GGSTUDIOS File Share"; ClientSize = new Size(900, 652); BackColor = BG; ForeColor = TXT; Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen; DoubleBuffered = true;
        Directory.CreateDirectory(dataDir);

        Lbl("SHARED FOLDERS  (drag folders here, or click Add)", 24, 92);
        lstFolders.SetBounds(24, 112, 300, 70); lstFolders.BackColor = SURF; lstFolders.ForeColor = TXT; lstFolders.BorderStyle = BorderStyle.FixedSingle; lstFolders.SelectionMode = SelectionMode.MultiExtended; lstFolders.HorizontalScrollbar = true; lstFolders.AllowDrop = true; Controls.Add(lstFolders);
        btnAddF = Btn("Add", 332, 112, 76, false); btnRemF = Btn("Remove", 332, 148, 76, false);
        Lbl("PORT", 24, 190); numPort.Minimum = 1; numPort.Maximum = 65535; numPort.Value = 8080; numPort.SetBounds(24, 208, 80, 26); numPort.BackColor = SURF; numPort.ForeColor = TXT; Controls.Add(numPort);
        Lbl("VIEWER PASSWORD (needed for auto-online)", 120, 190); Box(txtView, 120, 208, 288); txtView.UseSystemPasswordChar = true;
        Lbl("ADMIN PASSWORD (upload / delete)", 24, 240); Box(txtAdmin, 24, 258, 384); txtAdmin.UseSystemPasswordChar = true;
        chkRo.Text = "Read-only mode (no upload/delete)"; chkRo.SetBounds(24, 292, 384, 22); Controls.Add(chkRo);
        chkBoot.Text = "Start with Windows (hidden, tray only)"; chkBoot.SetBounds(24, 314, 384, 22); Controls.Add(chkBoot);
        chkAuto.Text = "Auto-start server + go online when the app opens"; chkAuto.SetBounds(24, 336, 384, 22); Controls.Add(chkAuto);
        btnStart = Btn("START SERVER", 24, 366, 190, true); btnTunnel = Btn("GO ONLINE", 224, 366, 184, false); btnTunnel.Enabled = false;

        Lbl("SCREEN SHARE (live, full resolution)", 436, 394);
        cmbEnc.DropDownStyle = ComboBoxStyle.DropDownList; cmbEnc.Items.AddRange(new object[] { "CPU (x264)", "NVIDIA GPU", "Intel GPU", "AMD GPU" }); cmbEnc.SelectedIndex = 0; cmbEnc.SetBounds(436, 412, 142, 26); Controls.Add(cmbEnc);
        cmbBr.DropDownStyle = ComboBoxStyle.DropDownList; cmbBr.Items.AddRange(new object[] { "8 Mbps", "15 Mbps", "25 Mbps", "40 Mbps" }); cmbBr.SelectedIndex = 1; cmbBr.SetBounds(586, 412, 142, 26); Controls.Add(cmbBr);
        cmbFps.DropDownStyle = ComboBoxStyle.DropDownList; cmbFps.Items.AddRange(new object[] { "15 FPS", "24 FPS", "30 FPS", "45 FPS", "60 FPS", "90 FPS", "120 FPS" }); cmbFps.SelectedIndex = 4; cmbFps.SetBounds(736, 412, 142, 26); Controls.Add(cmbFps);
        btnScreen = Btn("START SCREEN SHARE", 436, 446, 442, true); btnScreen.Enabled = false; btnScreen.Click += delegate { ToggleScreen(); };
        Box(urlScr, 436, 490, 350); urlScr.ReadOnly = true;
        var so = Btn("Open", 794, 486, 40, false); so.Text = "\u2197"; so.Height = 30; so.Click += delegate { if (urlScr.Text != "") Process.Start(urlScr.Text); };
        var sc = Btn("Copy", 838, 486, 40, false); sc.Text = "\u29C9"; sc.Height = 30; sc.Click += delegate { if (urlScr.Text != "") { Clipboard.SetText(urlScr.Text); Log("Screen link copied."); } };
        Lbl("LINKS", 24, 410);
        UrlRow("This PC", urlLocal, 432); UrlRow("Wi-Fi / LAN", urlNet, 464); UrlRow("Internet", urlOnline, 496, true);

        string[] names = { "DEVICES", "UPLOADED", "DOWNLOADED", "UPTIME" };
        for (int i = 0; i < 4; i++)
        {
            int x = 436 + (i % 2) * 226, y = 92 + (i / 2) * 84;
            Panel p = new Panel { Location = new Point(x, y), Size = new Size(216, 74), BackColor = SURF }; p.Paint += (s, e) => e.Graphics.DrawRectangle(new Pen(LINE), 0, 0, ((Panel)s).Width - 1, ((Panel)s).Height - 1);
            p.Controls.Add(new Label { Text = names[i], ForeColor = DIM, Location = new Point(12, 8), AutoSize = true, Font = new Font("Segoe UI", 8f) });
            tileVal[i] = new Label { Text = "0", ForeColor = ACC, Location = new Point(10, 28), AutoSize = true, Font = new Font("Segoe UI", 19f, FontStyle.Bold) };
            p.Controls.Add(tileVal[i]); Controls.Add(p);
        }
        Lbl("CONNECTED DEVICES (live)", 436, 268);
        lv.View = View.Details; lv.FullRowSelect = true; lv.BackColor = SURF; lv.ForeColor = TXT; lv.BorderStyle = BorderStyle.None; lv.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        lv.Columns.Add("IP", 110); lv.Columns.Add("Device", 100); lv.Columns.Add("Status", 90); lv.Columns.Add("Data", 90); lv.SetBounds(436, 290, 442, 96); Controls.Add(lv);
        Lbl("ACTIVITY", 24, 534);
        txtLog.Multiline = true; txtLog.ReadOnly = true; txtLog.ScrollBars = ScrollBars.Vertical; txtLog.SetBounds(24, 554, 854, 84); txtLog.BackColor = SURF; txtLog.ForeColor = TXT; txtLog.Font = new Font("Consolas", 9); txtLog.BorderStyle = BorderStyle.None; Controls.Add(txtLog);

        btnAddF.Click += delegate { using (var dlg = new FolderBrowserDialog()) { dlg.Description = "Pick a folder to share. Click Add again to add more folders."; if (dlg.ShowDialog() == DialogResult.OK) AddFolder(dlg.SelectedPath); } };
        btnRemF.Click += delegate { RemoveSelectedFolders(); };
        lstFolders.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) RemoveSelectedFolders(); };
        lstFolders.DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
        lstFolders.DragDrop += (s, e) => { var dropped = (string[])e.Data.GetData(DataFormats.FileDrop); foreach (var dp in dropped) if (Directory.Exists(dp)) AddFolder(dp); };
        btnStart.Click += delegate { if (server == null) Start(); else Stop(); };
        btnTunnel.Click += delegate { if (tunnel == null) GoOnline(); else StopTunnel(); };
        chkBoot.CheckedChanged += delegate { if (loading) return; SetBoot(chkBoot.Checked); if (chkBoot.Checked) chkAuto.Checked = true; };

        Icon = MakeIcon(); tray.Icon = Icon; tray.Text = "GGSTUDIOS - stopped"; tray.Visible = true;
        var menu = new ContextMenuStrip(); menu.Items.Add("Open", null, delegate { ShowMe(); });
        menu.Items.Add("Start / stop server", null, delegate { btnStart.PerformClick(); });
        menu.Items.Add("Go online / offline", null, delegate { btnTunnel.PerformClick(); });
        menu.Items.Add("Copy internet link", null, delegate { if (urlOnline.Text != "") Clipboard.SetText(urlOnline.Text); });
        menu.Items.Add("Exit", null, delegate { reallyExit = true; Close(); }); tray.ContextMenuStrip = menu;
        tray.DoubleClick += delegate { ShowMe(); };
        Resize += delegate { if (WindowState == FormWindowState.Minimized) { Hide(); WindowState = FormWindowState.Normal; } };
        FormClosing += (s, e) => { if (!reallyExit && e.CloseReason == CloseReason.UserClosing && (server != null || chkBoot.Checked || chkAuto.Checked)) { e.Cancel = true; Hide(); tray.ShowBalloonTip(2000, "GGSTUDIOS", "Still running in the tray.", ToolTipIcon.Info); } else { Stop(); tray.Visible = false; } };
        var showThread = new System.Threading.Thread(() => { while (true) { showEvt.WaitOne(); try { BeginInvoke(new Action(ShowMe)); } catch { } } }); showThread.IsBackground = true; showThread.Start();

        anim.Interval = 33; anim.Tick += delegate { if (++trayTick % 90 == 0) RefreshTray(); if (!Visible) return; phase += 0.05; for (int i = 0; i < 4; i++) { shown[i] += (target[i] - shown[i]) * 0.2; } ShowTiles(); Invalidate(new Rectangle(0, 0, Width, 84)); }; anim.Start();
        LoadSettings(); LoadTunnel();
        if (chkAuto.Checked) { bootT = new Timer { Interval = 2500 }; bootT.Tick += delegate { bootT.Stop(); AutoBoot(); }; bootT.Start(); }
    }

    protected override void SetVisibleCore(bool value)
    {
        if (startHidden) { if (!IsHandleCreated) CreateHandle(); value = false; }
        base.SetVisibleCore(value);
    }
    void ShowMe() { startHidden = false; Show(); WindowState = FormWindowState.Normal; Activate(); BringToFront(); }

    static Icon MakeIcon()
    {
        using (var bmp = new Bitmap(32, 32))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
                using (var br = new SolidBrush(ACC)) g.FillEllipse(br, 1, 1, 30, 30);
                using (var f = new Font("Segoe UI", 15f, FontStyle.Bold)) using (var tb = new SolidBrush(Color.FromArgb(0, 19, 26))) { var sf = new StringFormat(); sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center; g.DrawString("G", f, tb, new RectangleF(0, 0, 32, 32), sf); }
            }
            return Icon.FromHandle(bmp.GetHicon());
        }
    }
    void RefreshTray()
    {
        string t = !running ? "GGSTUDIOS - stopped" : (tunnel != null && urlOnline.Text != "") ? "GGSTUDIOS - online: " + urlOnline.Text.Replace("https://", "") : "GGSTUDIOS - running";
        if (t.Length > 62) t = t.Substring(0, 62); tray.Text = t;
    }
    void Warn(string m)
    {
        if (quiet) { Log("WARNING: " + m); try { tray.ShowBalloonTip(6000, "GGSTUDIOS", m, ToolTipIcon.Warning); } catch { } }
        else MessageBox.Show(m);
    }
    string[] FolderList() { var l = new List<string>(); foreach (object o in lstFolders.Items) l.Add(o.ToString()); return l.ToArray(); }
    void AddFolder(string p)
    {
        try { p = Path.GetFullPath(p); if (p.Length > 3) p = p.TrimEnd('\\'); } catch { }
        foreach (string x in FolderList()) if (string.Equals(x, p, StringComparison.OrdinalIgnoreCase)) return;
        lstFolders.Items.Add(p); Log("Folder added: " + p + (server != null ? "  (stop and start the server to apply)" : ""));
    }
    void RemoveSelectedFolders()
    {
        var sel = new List<object>(); foreach (object o in lstFolders.SelectedItems) sel.Add(o);
        foreach (object r in sel) lstFolders.Items.Remove(r);
        if (sel.Count > 0 && server != null) Log("Folders removed  (stop and start the server to apply)");
    }
    void AutoBoot()
    {
        quiet = true;
        try { Log("Auto-start: starting the server..."); Start(); if (running) GoOnline(); }
        finally { quiet = false; }
    }
    void ScheduleRestart(bool online, int ms)
    {
        if (restartT != null) { restartT.Stop(); restartT.Dispose(); }
        restartT = new Timer { Interval = ms };
        restartT.Tick += delegate { restartT.Stop(); quiet = true; try { Start(); if (running && online) GoOnline(); } finally { quiet = false; } };
        restartT.Start();
    }
    void RestartTunnel()
    {
        if (server == null) return;
        bool was = tunnel != null || wantOnline;
        if (tunnel != null) StopTunnel();
        if (!was) return;
        Log("Restarting the internet link (requested from the web page)...");
        var t = new Timer { Interval = 2000 };
        t.Tick += delegate { t.Stop(); t.Dispose(); if (server == null || tunnel != null) return; quiet = true; try { GoOnline(); } finally { quiet = false; } };
        t.Start();
    }
    void ScheduleTunnelRetry()
    {
        int wait = Math.Min(120, 10 * (1 << Math.Min(tunRetries, 4))); tunRetries++;
        Log("Reconnecting the link in " + wait + " seconds...");
        if (tunT != null) { tunT.Stop(); tunT.Dispose(); }
        tunT = new Timer { Interval = wait * 1000 };
        tunT.Tick += delegate { tunT.Stop(); if (!wantOnline || server == null || tunnel != null) return; quiet = true; try { GoOnline(); } finally { quiet = false; } };
        tunT.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        float sh = (float)((Math.Sin(phase * 0.6) + 1) / 2);
        using (var br = new LinearGradientBrush(new Rectangle(0, 0, Width, 84), Color.FromArgb(0, 40 + (int)(40 * sh), 70), Color.FromArgb(10, 14, 26), 0f)) g.FillRectangle(br, 0, 0, Width, 84);
        using (var f = new Font("Segoe UI", 24f, FontStyle.Bold)) g.DrawString("GGSTUDIOS", f, new SolidBrush(ACC), 22, 12);
        g.DrawString("FILE SHARE SERVER", new Font("Segoe UI", 8.5f), new SolidBrush(DIM), 26, 56);
        Color c = running ? Color.FromArgb(46, 230, 120) : Color.FromArgb(255, 90, 90);
        float pulse = running ? (float)((Math.Sin(phase * 3) + 1) / 2) : 0f;
        using (var br = new SolidBrush(Color.FromArgb((int)(70 * (1 - pulse)), c))) g.FillEllipse(br, 730 - 6 * pulse, 30 - 6 * pulse, 16 + 12 * pulse, 16 + 12 * pulse);
        g.FillEllipse(new SolidBrush(c), 736, 36, 12, 12);
        g.DrawString(running ? (tunnel != null ? "ONLINE" : "RUNNING") : "STOPPED", new Font("Segoe UI", 10f, FontStyle.Bold), new SolidBrush(c), 760, 33);
    }

    void Lbl(string t, int x, int y) { Controls.Add(new Label { Text = t, Location = new Point(x, y), AutoSize = true, ForeColor = DIM, Font = new Font("Segoe UI", 8f) }); }
    void Box(TextBox t, int x, int y, int w) { t.SetBounds(x, y, w, 26); t.BackColor = SURF; t.ForeColor = TXT; t.BorderStyle = BorderStyle.FixedSingle; Controls.Add(t); }
    Button Btn(string t, int x, int y, int w, bool primary)
    {
        var b = new Button { Text = t, Location = new Point(x, y), Size = new Size(w, 34), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
        b.BackColor = primary ? ACC : SURF; b.ForeColor = primary ? Color.FromArgb(0, 19, 26) : TXT; b.FlatAppearance.BorderColor = primary ? ACC : LINE;
        b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(90, 235, 255) : Color.FromArgb(31, 37, 53); if (primary) b.Font = new Font(Font, FontStyle.Bold); Controls.Add(b); return b;
    }
    void UrlRow(string name, TextBox t, int y, bool gear = false)
    {
        int bw = gear ? 188 : 224, ox = gear ? 304 : 340, cx = gear ? 340 : 376;
        Controls.Add(new Label { Text = name, Location = new Point(24, y + 4), AutoSize = true, ForeColor = TXT }); Box(t, 110, y, bw); t.ReadOnly = true;
        var o = Btn("Open", ox, y - 4, 32, false); o.Text = "\u2197"; o.Height = 30; o.Click += delegate { if (t.Text != "") Process.Start(t.Text); };
        var c = Btn("Copy", cx, y - 4, 32, false); c.Text = "\u29C9"; c.Height = 30; c.Click += delegate { if (t.Text != "") { Clipboard.SetText(t.Text); Log("Link copied."); } };
        if (gear) { var g = Btn("Settings", 376, y - 4, 32, false); g.Text = "\u2699"; g.Height = 30; g.Click += delegate { TunnelSettings(); }; }
    }

    void Log(string m) { if (IsDisposed) return; if (InvokeRequired) { try { BeginInvoke(new Action<string>(Log), m); } catch { } return; } txtLog.AppendText(DateTime.Now.ToString("HH:mm:ss  ") + m + "\r\n"); }

    static string Prot(string s) { if (s == "") return ""; return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(s), null, DataProtectionScope.CurrentUser)); }
    static string Unprot(string s) { try { return s == "" ? "" : Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(s), null, DataProtectionScope.CurrentUser)); } catch { return ""; } }
    string SettingsFile { get { return Path.Combine(dataDir, "settings.txt"); } }
    static readonly string[] ENCS = { "libx264", "h264_nvenc", "h264_qsv", "h264_amf" }, BRS = { "8M", "15M", "25M", "40M" }, FPSS = { "15", "24", "30", "45", "60", "90", "120" };
    static bool IsBootEnabled() { try { using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) { return k != null && k.GetValue("GGShare") != null; } } catch { return false; } }
    void LoadSettings()
    {
        loading = true;
        try { var l = File.ReadAllLines(SettingsFile); foreach (var fp in l[0].Split('|')) if (fp.Trim() != "") lstFolders.Items.Add(fp.Trim()); numPort.Value = int.Parse(l[1]); txtView.Text = Unprot(l[2]); txtAdmin.Text = Unprot(l[3]); chkRo.Checked = l[4] == "1"; chkBoot.Checked = l[5] == "1"; } catch { }
        try { var l = File.ReadAllLines(SettingsFile); cmbEnc.SelectedIndex = int.Parse(l[6]); cmbBr.SelectedIndex = int.Parse(l[7]); cmbFps.SelectedIndex = int.Parse(l[8]); } catch { }
        try { var l = File.ReadAllLines(SettingsFile); chkAuto.Checked = l[9] == "1"; } catch { }
        if (lstFolders.Items.Count == 0) lstFolders.Items.Add(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads");
        chkBoot.Checked = IsBootEnabled(); if (!File.Exists(SettingsFile) && chkBoot.Checked) chkAuto.Checked = true;
        loading = false; if (chkBoot.Checked) SetBoot(true);
    }
    void SaveSettings() { try { File.WriteAllLines(SettingsFile, new[] { string.Join("|", FolderList()), numPort.Value.ToString(), Prot(txtView.Text), Prot(txtAdmin.Text), chkRo.Checked ? "1" : "0", chkBoot.Checked ? "1" : "0", cmbEnc.SelectedIndex.ToString(), cmbBr.SelectedIndex.ToString(), cmbFps.SelectedIndex.ToString(), chkAuto.Checked ? "1" : "0" }); } catch { } }
    void SetBoot(bool on)
    {
        try { using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true)) { if (on) k.SetValue("GGShare", "\"" + Application.ExecutablePath + "\" --tray"); else k.DeleteValue("GGShare", false); } } catch (Exception ex) { Log("Startup setting failed: " + ex.Message); }
    }

    string FreshPath()
    {
        return (Environment.GetEnvironmentVariable("PATH") ?? "") + ";" + (Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) ?? "") + ";" +
            (Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "") + ";" + Environment.GetEnvironmentVariable("LOCALAPPDATA") + @"\Microsoft\WinGet\Links";
    }
    string FindFfmpeg()
    {
        string f = FindExe("ffmpeg.exe", new[] { @"C:\ffmpeg\bin\ffmpeg.exe", @"C:\Program Files\ffmpeg\bin\ffmpeg.exe" });
        if (f != null) return f;
        try
        {
            string pk = Environment.GetEnvironmentVariable("LOCALAPPDATA") + @"\Microsoft\WinGet\Packages";
            if (Directory.Exists(pk)) foreach (var d in Directory.GetDirectories(pk, "*ffmpeg*")) { var hit = Directory.GetFiles(d, "ffmpeg.exe", SearchOption.AllDirectories); if (hit.Length > 0) return hit[0]; }
        }
        catch { }
        return null;
    }
    string FindExe(string name, string[] extra)
    {
        foreach (var p in FreshPath().Split(';')) { try { var f = Path.Combine(p.Trim('"'), name); if (File.Exists(f)) return f; } catch { } }
        foreach (var f in extra) if (File.Exists(f)) return f; return null;
    }
    string LanIp()
    {
        foreach (var a in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
            if (a.AddressFamily == AddressFamily.InterNetwork && (a.ToString().StartsWith("192.168.") || a.ToString().StartsWith("10.") || Regex.IsMatch(a.ToString(), @"^172\.(1[6-9]|2\d|3[01])\."))) return a.ToString();
        return "localhost";
    }
    int FreePort(int p) { for (; p < 65535; p++) { try { var l = new TcpListener(IPAddress.Any, p); l.Start(); l.Stop(); return p; } catch { } } return 8080; }
    void Extract(string name) { using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)) using (var f = File.Create(Path.Combine(dataDir, name))) s.CopyTo(f); }

    void Start()
    {
        string[] folders = FolderList();
        if (folders.Length == 0) { Warn("Add at least one folder to share first."); return; }
        foreach (string fo in folders) if (!Directory.Exists(fo)) Log("Folder not available right now: " + fo + "  (it will appear when it is back)");
        string node = FindExe("node.exe", new[] { @"C:\Program Files\nodejs\node.exe", @"C:\Program Files (x86)\nodejs\node.exe" });
        if (node == null) { if (quiet) Warn("Node.js is not installed, so the server cannot start. Install it from nodejs.org."); else if (MessageBox.Show("Node.js is required (free, one-time install).\r\nOpen the download page?", "Node.js not found", MessageBoxButtons.YesNo) == DialogResult.Yes) Process.Start("https://nodejs.org"); return; }
        int want = (int)numPort.Value, port = FreePort(want);
        if (port != want) { Log("Port " + want + " was busy - using " + port + " instead."); numPort.Value = port; }
        SaveSettings(); Extract("server.js"); Extract("index.html");
        var psi = new ProcessStartInfo(node, "\"" + Path.Combine(dataDir, "server.js") + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
        psi.EnvironmentVariables["GG_ROOT"] = folders[0]; psi.EnvironmentVariables["GG_ROOTS"] = js.Serialize(folders); psi.EnvironmentVariables["GG_PORT"] = port.ToString(); psi.EnvironmentVariables["GG_UI"] = Path.Combine(dataDir, "index.html");
        psi.EnvironmentVariables["GG_VIEW"] = txtView.Text; psi.EnvironmentVariables["GG_ADMIN"] = txtAdmin.Text; psi.EnvironmentVariables["GG_RO"] = chkRo.Checked ? "1" : "0"; psi.EnvironmentVariables["PATH"] = FreshPath();
        psi.EnvironmentVariables["GG_ENC"] = ENCS[cmbEnc.SelectedIndex]; psi.EnvironmentVariables["GG_BR"] = BRS[cmbBr.SelectedIndex]; psi.EnvironmentVariables["GG_FPS"] = FPSS[cmbFps.SelectedIndex]; psi.EnvironmentVariables["GG_MANAGED"] = "1";
        { string ff0 = FindFfmpeg(); if (ff0 != null) psi.EnvironmentVariables["GG_FFMPEG"] = ff0; }
        try
        {
            server = new Process { StartInfo = psi, EnableRaisingEvents = true };
            server.OutputDataReceived += (s, e) => { if (e.Data == null) return; if (e.Data.StartsWith("@@STATS ")) { try { BeginInvoke(new Action<string>(OnStats), e.Data.Substring(8)); } catch { } } else if (e.Data == "@@RESTART") { webRestart = true; Log("Restart requested from the web page."); }
                else if (e.Data == "@@SHUTDOWN") { wantServer = false; wantOnline = false; Log("Shutdown requested from the web page."); }
                else if (e.Data == "@@TUNNEL") { try { BeginInvoke(new Action(RestartTunnel)); } catch { } }
                else { Log(e.Data); if (screenOn && e.Data.StartsWith("SCREEN sharing stopped")) { try { BeginInvoke(new Action(() => { if (!screenOn) return; screenOn = false; cmbEnc.Enabled = cmbBr.Enabled = cmbFps.Enabled = true; btnScreen.Text = "START SCREEN SHARE"; urlScr.Text = ""; Log("Screen sharing stopped. Check the [screen] lines above if it was not you."); })); } catch { } } } };
            server.ErrorDataReceived += (s, e) => { if (e.Data != null) Log("ERROR: " + e.Data); };
            server.Exited += delegate { try { BeginInvoke(new Action(ServerStopped)); } catch { } };
            server.Start(); server.BeginOutputReadLine(); server.BeginErrorReadLine();
        }
        catch (Exception ex) { Log("Could not start: " + ex.Message); server = null; return; }
        running = true; wantServer = true; btnStart.Text = "STOP SERVER"; btnTunnel.Enabled = true; btnScreen.Enabled = true;
        urlLocal.Text = "http://localhost:" + port; urlNet.Text = "http://" + LanIp() + ":" + port; urlOnline.Text = "";
        Log("Server running: " + urlNet.Text); RefreshTray();
        if (txtAdmin.Text == "" && txtView.Text == "") Log("WARNING: no passwords set - anyone with a link can upload and delete.");
    }

    void OnStats(string json)
    {
        try
        {
            var d = js.Deserialize<Dictionary<string, object>>(json); var devs = (ArrayList)d["devices"];
            target[0] = devs.Count; target[1] = Convert.ToDouble(d["up"]); target[2] = Convert.ToDouble(d["down"]); target[3] = Convert.ToDouble(d["uptime"]);
            lv.BeginUpdate(); lv.Items.Clear();
            foreach (Dictionary<string, object> x in devs)
            {
                var it = new ListViewItem(x["ip"].ToString()); it.SubItems.Add(x["ua"].ToString());
                int age = Convert.ToInt32(x["age"]); it.SubItems.Add(age < 10 ? "● active" : "idle " + age + "s"); it.SubItems.Add(Fmt(Convert.ToDouble(x["bytes"])));
                if (age < 10) it.ForeColor = Color.FromArgb(46, 230, 120); lv.Items.Add(it);
            }
            lv.EndUpdate();
        }
        catch { }
    }
    static string Fmt(double b) { string[] u = { "B", "KB", "MB", "GB", "TB" }; int i = 0; while (b >= 1024 && i < 4) { b /= 1024; i++; } return (i == 0 ? b.ToString("0") : b.ToString("0.0")) + " " + u[i]; }
    void ShowTiles()
    {
        tileVal[0].Text = Math.Round(shown[0]).ToString(); tileVal[1].Text = Fmt(shown[1]); tileVal[2].Text = Fmt(shown[2]);
        var t = TimeSpan.FromSeconds(Math.Round(shown[3])); tileVal[3].Text = t.Hours + t.Days * 24 > 0 ? ((int)t.TotalHours) + "h " + t.Minutes + "m" : t.Minutes + "m " + t.Seconds + "s";
    }

    void ServerStopped()
    {
        bool resume = wantServer, resumeOn = wantOnline; wantServer = false;
        server = null; running = false; screenOn = false; cmbEnc.Enabled = cmbBr.Enabled = cmbFps.Enabled = true; btnScreen.Enabled = false; btnScreen.Text = "START SCREEN SHARE"; urlScr.Text = ""; StopTunnel(); btnStart.Text = "START SERVER"; btnTunnel.Enabled = false; urlLocal.Text = urlNet.Text = urlOnline.Text = "";
        for (int i = 0; i < 4; i++) target[i] = 0; lv.Items.Clear(); Log("Server stopped."); RefreshTray();
        if (resume && !reallyExit) { bool byWeb = webRestart; webRestart = false; Log(byWeb ? "Restarting the server..." : "The server stopped unexpectedly - restarting in 5 seconds..."); ScheduleRestart(resumeOn, byWeb ? 2000 : 5000); }
    }
    void ToggleScreen()
    {
        if (server == null) return;
        string ffp = FindFfmpeg();
        if (ffp == null) { MessageBox.Show("ffmpeg is required for screen sharing.\r\nRun in Command Prompt:\r\n\r\nwinget install ffmpeg\r\n\r\nThen click Start Screen Share again."); return; }
        screenOn = !screenOn;
        server.StandardInput.WriteLine(screenOn ? "screen on " + ENCS[cmbEnc.SelectedIndex] + " " + BRS[cmbBr.SelectedIndex] + " " + FPSS[cmbFps.SelectedIndex] + " " + ffp : "screen off");
        cmbEnc.Enabled = cmbBr.Enabled = cmbFps.Enabled = !screenOn; if (screenOn) SaveSettings();
        btnScreen.Text = screenOn ? "STOP SCREEN SHARE" : "START SCREEN SHARE";
        urlScr.Text = screenOn ? (urlNet.Text + "/__screen") : "";
        Log(screenOn ? "Screen sharing ON (" + cmbEnc.Text + ", " + cmbBr.Text + ", " + cmbFps.Text + "). Stop sharing to change these." : "Screen sharing OFF.");
    }
    void Stop() { wantServer = false; wantOnline = false; if (restartT != null) restartT.Stop(); if (tunnel != null) StopTunnel(); if (server != null) KillTree(server); }
    void KillTree(Process p) { try { if (!p.HasExited) Process.Start(new ProcessStartInfo("taskkill", "/PID " + p.Id + " /T /F") { CreateNoWindow = true, UseShellExecute = false }).WaitForExit(2000); } catch { } }

    int tunMode = 0; string cfToken = "", cfHost = "", ngDomain = "", ngToken = "";
    string TunFile { get { return Path.Combine(dataDir, "tunnel.txt"); } }
    void LoadTunnel() { try { var l = File.ReadAllLines(TunFile); tunMode = int.Parse(l[0]); cfToken = Unprot(l[1]); cfHost = l[2]; ngDomain = l[3]; ngToken = Unprot(l[4]); } catch { } }
    void SaveTunnel() { try { File.WriteAllLines(TunFile, new[] { tunMode.ToString(), Prot(cfToken), cfHost, ngDomain, Prot(ngToken) }); } catch { } }
    static string CleanHost(string s) { s = (s ?? "").Trim(); s = Regex.Replace(s, "^https?://", "", RegexOptions.IgnoreCase); int i = s.IndexOfAny(new[] { '/', ' ' }); if (i >= 0) s = s.Substring(0, i); return s.ToLower(); }

    void TunnelSettings()
    {
        if (tunnel != null) { MessageBox.Show("Click GO OFFLINE first, then change the link settings."); return; }
        var f = new Form { Text = "Online link settings", ClientSize = new Size(480, 500), BackColor = BG, ForeColor = TXT, Font = Font, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false };
        Func<string, int, bool, RadioButton> rbm = (t, y, chk) => { var r = new RadioButton { Text = t, Location = new Point(20, y), Size = new Size(440, 24), Checked = chk, ForeColor = TXT, Font = new Font("Segoe UI", 10f, FontStyle.Bold) }; f.Controls.Add(r); return r; };
        Func<string, int, Label> lb = (t, y) => { var l = new Label { Text = t, Location = new Point(40, y), Size = new Size(420, 18), ForeColor = DIM, Font = new Font("Segoe UI", 8.5f) }; f.Controls.Add(l); return l; };
        Func<int, string, bool, TextBox> tbm = (y, v, pw) => { var t = new TextBox { Location = new Point(40, y), Size = new Size(420, 26), BackColor = SURF, ForeColor = TXT, BorderStyle = BorderStyle.FixedSingle, Text = v, UseSystemPasswordChar = pw }; f.Controls.Add(t); return t; };

        var rb0 = rbm("Random link  (free, new address every time)", 14, tunMode == 0);
        var rb1 = rbm("Fixed / custom / short link  (Cloudflare, your own domain)", 48, tunMode == 1);
        lb("Tunnel token", 76); var tCfTok = tbm(96, cfToken, true);
        lb("Your link, e.g.  go.mydomain.com", 128); var tCfHost = tbm(148, cfHost, false);
        var hint1 = new Label { Text = "Needs a free Cloudflare account and a domain. In Cloudflare Zero Trust > Networks > Tunnels, create a tunnel, copy its token, then add a Public Hostname that points to  http://localhost:" + numPort.Value + "  (your port). A short domain gives a short link.", Location = new Point(40, 180), Size = new Size(420, 62), ForeColor = DIM, Font = new Font("Segoe UI", 8.5f) }; f.Controls.Add(hint1);
        var rb2 = rbm("Fixed link  (ngrok, free, no domain needed)", 256, tunMode == 2);
        lb("Your free static domain, e.g.  name.ngrok-free.app", 284); var tNgDom = tbm(304, ngDomain, false);
        lb("ngrok authtoken (needed once)", 336); var tNgTok = tbm(356, ngToken, true);
        var hint2 = new Label { Text = "Get both at dashboard.ngrok.com (Domains + Your Authtoken). Free ngrok shows a one-time warning page and has a monthly data limit, so it is not ideal for live screen video.", Location = new Point(40, 388), Size = new Size(420, 48), ForeColor = DIM, Font = new Font("Segoe UI", 8.5f) }; f.Controls.Add(hint2);
        Action sync = delegate { tCfTok.Enabled = tCfHost.Enabled = rb1.Checked; tNgDom.Enabled = tNgTok.Enabled = rb2.Checked; };
        rb0.CheckedChanged += delegate { sync(); }; rb1.CheckedChanged += delegate { sync(); }; rb2.CheckedChanged += delegate { sync(); }; sync();
        var ok = new Button { Text = "SAVE", Location = new Point(20, 448), Size = new Size(200, 34), FlatStyle = FlatStyle.Flat, BackColor = ACC, ForeColor = Color.FromArgb(0, 19, 26), Cursor = Cursors.Hand }; ok.FlatAppearance.BorderColor = ACC;
        var cancel = new Button { Text = "Cancel", Location = new Point(232, 448), Size = new Size(228, 34), FlatStyle = FlatStyle.Flat, BackColor = SURF, ForeColor = TXT, Cursor = Cursors.Hand }; cancel.FlatAppearance.BorderColor = LINE;
        cancel.Click += delegate { f.Close(); };
        ok.Click += delegate
        {
            int m = rb1.Checked ? 1 : rb2.Checked ? 2 : 0;
            if (m == 1 && (tCfTok.Text.Trim() == "" || CleanHost(tCfHost.Text) == "")) { MessageBox.Show("Enter both the tunnel token and your link (hostname)."); return; }
            if (m == 2 && CleanHost(tNgDom.Text) == "") { MessageBox.Show("Enter your ngrok static domain."); return; }
            tunMode = m; cfToken = tCfTok.Text.Trim(); cfHost = CleanHost(tCfHost.Text); ngDomain = CleanHost(tNgDom.Text); ngToken = tNgTok.Text.Trim();
            SaveTunnel(); Log("Online link mode: " + (m == 0 ? "random" : m == 1 ? "fixed (Cloudflare) " + cfHost : "fixed (ngrok) " + ngDomain)); f.Close();
        };
        f.Controls.Add(ok); f.Controls.Add(cancel);
        f.ShowDialog(this);
    }

    void GoOnline()
    {
        if (txtView.Text == "")
        {
            if (quiet) { Warn("Auto-online skipped: set a VIEWER password first, then click GO ONLINE (otherwise anyone with the link could open your files)."); return; }
            if (MessageBox.Show("No VIEWER password is set, so anyone with the internet link can browse and download your files.\r\nContinue anyway?", "Security warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        }
        ProcessStartInfo psi;
        if (tunMode == 2)
        {
            string ng = FindExe("ngrok.exe", new[] { Environment.GetEnvironmentVariable("LOCALAPPDATA") + @"\Microsoft\WinGet\Links\ngrok.exe", @"C:\Program Files\ngrok\ngrok.exe" });
            if (ng == null) { Warn("ngrok is not installed.\r\nRun in Command Prompt:\r\n\r\nwinget install ngrok.ngrok\r\n\r\nThen click GO ONLINE again."); return; }
            if (ngToken != "")
            {
                try
                {
                    Log("Saving ngrok authtoken...");
                    var cfg = Process.Start(new ProcessStartInfo(ng, "config add-authtoken " + ngToken) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true });
                    string outp = cfg.StandardOutput.ReadToEnd() + cfg.StandardError.ReadToEnd(); cfg.WaitForExit(8000);
                    outp = outp.Trim(); if (outp != "") Log("[ngrok] " + (outp.Length > 200 ? outp.Substring(0, 200) : outp));
                }
                catch (Exception ex) { Log("[ngrok] could not save authtoken: " + ex.Message); }
            }
            psi = new ProcessStartInfo(ng, "http --domain=" + ngDomain + " --log=stdout " + numPort.Value) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        }
        else
        {
            string cf = FindExe("cloudflared.exe", new[] { @"C:\Program Files (x86)\cloudflared\cloudflared.exe", @"C:\Program Files\cloudflared\cloudflared.exe", Environment.GetEnvironmentVariable("LOCALAPPDATA") + @"\Microsoft\WinGet\Links\cloudflared.exe" });
            if (cf == null) { Warn("cloudflared is not installed.\r\nRun in Command Prompt:\r\n\r\nwinget install Cloudflare.cloudflared\r\n\r\nThen restart this app."); return; }
            psi = new ProcessStartInfo(cf, tunMode == 1 ? "tunnel --no-autoupdate run" : "tunnel --no-autoupdate --url http://localhost:" + numPort.Value) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            if (tunMode == 1) psi.EnvironmentVariables["TUNNEL_TOKEN"] = cfToken;
        }
        var tp = new Process { StartInfo = psi, EnableRaisingEvents = true }; tunnel = tp;
        tp.Exited += delegate
        {
            try { BeginInvoke(new Action(() => { if (tunnel != tp) return; int code = -1; try { code = tp.ExitCode; } catch { } tunnel = null; btnTunnel.Text = "GO ONLINE"; urlOnline.Text = ""; Log("Tunnel stopped by itself (exit code " + code + "). Read the [ngrok]/[tunnel] lines above for the reason."); RefreshTray(); if (wantOnline && server != null) ScheduleTunnelRetry(); })); } catch { }
        };
        bool announced = false;
        DataReceivedEventHandler h = (s, e) =>
        {
            if (e.Data == null) return;
            string d = e.Data;
            if (tunMode == 0) { var m = Regex.Match(d, @"https://[a-z0-9-]+\.trycloudflare\.com"); if (m.Success) BeginInvoke(new Action(() => { urlOnline.Text = m.Value; Log("ONLINE: " + m.Value); tunRetries = 0; RefreshTray(); })); }
            else if (tunMode == 1)
            {
                if (!announced && d.Contains("Registered tunnel connection")) { announced = true; BeginInvoke(new Action(() => { urlOnline.Text = "https://" + cfHost; Log("ONLINE: https://" + cfHost); tunRetries = 0; RefreshTray(); })); }
                else if (d.Trim() != "") BeginInvoke(new Action(() => Log("[tunnel] " + (d.Length > 180 ? d.Substring(0, 180) : d))));
            }
            else
            {
                if (!announced && d.Contains("started tunnel")) { announced = true; BeginInvoke(new Action(() => { urlOnline.Text = "https://" + ngDomain; Log("ONLINE: https://" + ngDomain); tunRetries = 0; RefreshTray(); })); }
                else if (d.Trim() != "") BeginInvoke(new Action(() => Log("[ngrok] " + (d.Length > 200 ? d.Substring(0, 200) : d))));
            }
        };
        tp.ErrorDataReceived += h; tp.OutputDataReceived += h;
        try { tp.Start(); tp.BeginErrorReadLine(); tp.BeginOutputReadLine(); wantOnline = true; }
        catch (Exception ex2) { Log("Could not start the tunnel: " + ex2.Message); tunnel = null; return; }
        btnTunnel.Text = "GO OFFLINE"; Log(tunMode == 0 ? "Starting tunnel (random link)... appears in a few seconds." : "Starting tunnel (fixed link)... appears in a few seconds.");
    }
    void StopTunnel() { wantOnline = false; if (tunT != null) tunT.Stop(); if (tunnel == null) return; var tt = tunnel; tunnel = null; KillTree(tt); btnTunnel.Text = "GO ONLINE"; urlOnline.Text = ""; Log("Tunnel closed."); RefreshTray(); }
}
