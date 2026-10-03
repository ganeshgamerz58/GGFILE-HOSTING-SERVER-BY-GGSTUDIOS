using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

static class Info
{
    public const string Name = "GGSTUDIOS File Share", AppExe = "GGShare.exe", Version = "1.1.0", Publisher = "GGSTUDIOS";
    public const string UninstKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\GGShare";
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
}

class Program
{
    [STAThread]
    static void Main(string[] a)
    {
        Application.EnableVisualStyles();
        if (Array.IndexOf(a, "/uninstall") >= 0) { Uninstaller.Run(a); return; }
        Application.Run(new SetupForm());
    }
}

static class Util
{
    public static void KillApp()
    {
        try { var p = Process.Start(new ProcessStartInfo("taskkill", "/F /T /IM " + Info.AppExe) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true }); p.WaitForExit(6000); } catch { }
        Thread.Sleep(900);
    }

    public static void MakeShortcut(string lnk, string target, string workDir, string description)
    {
        Type t = Type.GetTypeFromProgID("WScript.Shell");
        object sh = Activator.CreateInstance(t);
        object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
        Type st = sc.GetType();
        st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
        st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { workDir });
        st.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { description });
        st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { target + ",0" });
        st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
    }

    public static string StartMenuDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "GGSTUDIOS"); } }
    public static string DesktopLnk { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), Info.Name + ".lnk"); } }

    public static string FindOnPath(string exe, params string[] extra)
    {
        string all = (Environment.GetEnvironmentVariable("PATH") ?? "") + ";" + (Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) ?? "") + ";" + (Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "") + ";" + Environment.GetEnvironmentVariable("LOCALAPPDATA") + @"\Microsoft\WinGet\Links";
        foreach (string p in all.Split(';')) { try { string f = Path.Combine(p.Trim('"'), exe); if (p.Trim() != "" && File.Exists(f)) return f; } catch { } }
        foreach (string f in extra) { try { if (File.Exists(f)) return f; } catch { } }
        return null;
    }

    public static bool HaveFfmpeg()
    {
        if (FindOnPath("ffmpeg.exe", @"C:\ffmpeg\bin\ffmpeg.exe", @"C:\Program Files\ffmpeg\bin\ffmpeg.exe") != null) return true;
        try
        {
            string pk = Environment.GetEnvironmentVariable("LOCALAPPDATA") + @"\Microsoft\WinGet\Packages";
            if (Directory.Exists(pk)) foreach (string d in Directory.GetDirectories(pk, "*FFmpeg*")) if (Directory.GetFiles(d, "ffmpeg.exe", SearchOption.AllDirectories).Length > 0) return true;
        }
        catch { }
        return false;
    }

    // runs a console program, streams its output lines; returns exit code (-1 if it could not start)
    public static int Run(string file, string args, Action<string> onLine)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
            using (var p = Process.Start(psi))
            {
                DataReceivedEventHandler h = (s, e) =>
                {
                    if (e.Data == null) return;
                    string l = e.Data.Trim();
                    if (l.Length < 4 || l.IndexOf('\u2588') >= 0 || l.IndexOf('\u2592') >= 0) return;
                    onLine(l.Length > 110 ? l.Substring(0, 110) : l);
                };
                p.OutputDataReceived += h; p.ErrorDataReceived += h; p.BeginOutputReadLine(); p.BeginErrorReadLine();
                p.WaitForExit();
                return p.ExitCode;
            }
        }
        catch (Exception ex) { onLine("Could not run " + file + ": " + ex.Message); return -1; }
    }
}

class SetupForm : Form
{
    static Color BG = Color.FromArgb(10, 14, 26), SURF = Color.FromArgb(20, 24, 36), LINE = Color.FromArgb(31, 37, 53), TXT = Color.FromArgb(232, 237, 245), DIM = Color.FromArgb(138, 146, 168), ACC = Color.FromArgb(0, 217, 255);
    Panel[] pages = new Panel[5];
    int cur = 0; bool installing = false, success = false;
    Button btnBack, btnNext, btnCancel, btnBrowse;
    TextBox txtDir = new TextBox(), txtLog = new TextBox();
    CheckBox chkDesk = new CheckBox(), chkMenu = new CheckBox(), chkAuto = new CheckBox(), chkNode = new CheckBox(), chkFfmpeg = new CheckBox(), chkNgrok = new CheckBox(), chkLaunch = new CheckBox();
    ProgressBar bar = new ProgressBar();
    Label lblResult;
    bool haveNode, haveFfmpeg, haveNgrok; string installedExe = "";

    public SetupForm()
    {
        Text = Info.Name + " Setup"; ClientSize = new Size(640, 450); BackColor = BG; ForeColor = TXT; Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterScreen; DoubleBuffered = true;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        haveNode = Util.FindOnPath("node.exe", @"C:\Program Files\nodejs\node.exe", @"C:\Program Files (x86)\nodejs\node.exe") != null;
        haveFfmpeg = Util.HaveFfmpeg();
        haveNgrok = Util.FindOnPath("ngrok.exe", @"C:\Program Files\ngrok\ngrok.exe") != null;

        for (int i = 0; i < pages.Length; i++) { pages[i] = new Panel { Location = new Point(0, 76), Size = new Size(640, 320), BackColor = BG }; Controls.Add(pages[i]); }

        // ---- page 0: welcome ----
        Text_(pages[0], "Welcome to the setup", 28, 20, 580, 28, 15f, true, false);
        Text_(pages[0], "This installs " + Info.Name + " on your computer like any other program. Once installed it can start with Windows, run hidden in the tray, and put your folders online on your fixed link.", 28, 62, 580, 60, 10f, false, true);
        Text_(pages[0], "What you need:", 28, 134, 580, 22, 10f, true, false);
        Text_(pages[0], "\u2022  Node.js  (runs the server)\r\n\u2022  ffmpeg  (only for live screen sharing)\r\n\u2022  ngrok or cloudflared  (only for the internet link)\r\n\r\nThe next steps can install these for you automatically.", 28, 158, 580, 130, 10f, false, true);

        // ---- page 1: location ----
        Text_(pages[1], "Where should it be installed?", 28, 20, 580, 28, 15f, true, false);
        Text_(pages[1], "Setup will install " + Info.Name + " in this folder. Click Next to continue, or Browse to pick a different folder.", 28, 62, 580, 44, 10f, false, true);
        string def = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"GGSTUDIOS\GGShare");
        try { using (var k = Registry.LocalMachine.OpenSubKey(Info.UninstKey)) { if (k != null && k.GetValue("InstallLocation") != null) def = k.GetValue("InstallLocation").ToString(); } } catch { }
        txtDir.Text = def; txtDir.SetBounds(28, 120, 470, 26); txtDir.BackColor = SURF; txtDir.ForeColor = TXT; txtDir.BorderStyle = BorderStyle.FixedSingle; pages[1].Controls.Add(txtDir);
        btnBrowse = Mk("Browse...", 508, 118, 100, false); pages[1].Controls.Add(btnBrowse);
        btnBrowse.Click += delegate { using (var d = new FolderBrowserDialog()) { d.SelectedPath = txtDir.Text; if (d.ShowDialog() == DialogResult.OK) txtDir.Text = Path.Combine(d.SelectedPath, d.SelectedPath.ToLower().EndsWith("ggshare") ? "" : "GGShare"); } };
        Text_(pages[1], "If you install an older version again, Setup updates it and keeps your settings.", 28, 164, 580, 40, 9f, false, true);

        // ---- page 2: options ----
        Text_(pages[2], "Choose your options", 28, 14, 580, 28, 15f, true, false);
        Chk(pages[2], chkDesk, "Create a desktop shortcut", 32, 54, true);
        Chk(pages[2], chkMenu, "Create a Start menu shortcut", 32, 80, true);
        Chk(pages[2], chkAuto, "Start automatically when Windows starts  (hidden, tray only)", 32, 106, true);
        Text_(pages[2], "REQUIRED COMPONENTS  (downloaded with Windows Package Manager, needs internet)", 30, 146, 580, 18, 8f, false, false);
        Chk(pages[2], chkNode, haveNode ? "Node.js is already installed" : "Install Node.js  (required)", 32, 170, !haveNode); chkNode.Enabled = !haveNode;
        Chk(pages[2], chkFfmpeg, haveFfmpeg ? "ffmpeg is already installed" : "Install ffmpeg  (for live screen sharing)", 32, 196, !haveFfmpeg); chkFfmpeg.Enabled = !haveFfmpeg;
        Chk(pages[2], chkNgrok, haveNgrok ? "Update ngrok  (the fixed link needs version 3.20 or newer)" : "Install ngrok  (for the fixed internet link)", 32, 222, !haveNgrok);
        Text_(pages[2], "Skip a component if you already have it or prefer to install it yourself.", 30, 256, 580, 20, 9f, false, false);

        // ---- page 3: installing ----
        Text_(pages[3], "Installing...", 28, 14, 580, 28, 15f, true, false);
        bar.SetBounds(28, 54, 584, 18); bar.Style = ProgressBarStyle.Marquee; bar.MarqueeAnimationSpeed = 30; pages[3].Controls.Add(bar);
        txtLog.Multiline = true; txtLog.ReadOnly = true; txtLog.ScrollBars = ScrollBars.Vertical; txtLog.SetBounds(28, 84, 584, 214); txtLog.BackColor = SURF; txtLog.ForeColor = TXT; txtLog.Font = new Font("Consolas", 9f); txtLog.BorderStyle = BorderStyle.None; pages[3].Controls.Add(txtLog);

        // ---- page 4: finish ----
        Text_(pages[4], "All done", 28, 20, 580, 28, 15f, true, false);
        lblResult = Text_(pages[4], "", 28, 62, 580, 120, 10f, false, true);
        Chk(pages[4], chkLaunch, "Open " + Info.Name + " now", 32, 200, true);

        // ---- footer ----
        btnBack = Mk("< Back", 318, 408, 96, false); btnNext = Mk("Next >", 422, 408, 96, true); btnCancel = Mk("Cancel", 526, 408, 96, false);
        Controls.Add(btnBack); Controls.Add(btnNext); Controls.Add(btnCancel);
        btnBack.Click += delegate { if (cur > 0 && cur < 3) GoPage(cur - 1); };
        btnNext.Click += delegate { Next(); };
        btnCancel.Click += delegate { Close(); };
        FormClosing += (s, e) =>
        {
            if (installing) { e.Cancel = true; return; }
            if (cur < 4 && e.CloseReason == CloseReason.UserClosing && MessageBox.Show("Cancel the setup?", Info.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) e.Cancel = true;
        };
        GoPage(0);
    }

    Label Text_(Panel p, string t, int x, int y, int w, int h, float size, bool bold, bool dim)
    {
        var l = new Label { Text = t, Location = new Point(x, y), Size = new Size(w, h), ForeColor = dim ? DIM : TXT, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular) };
        if (!bold && !dim && size <= 8f) l.ForeColor = DIM;
        p.Controls.Add(l); return l;
    }
    void Chk(Panel p, CheckBox c, string t, int x, int y, bool on) { c.Text = t; c.SetBounds(x, y, 580, 22); c.Checked = on; c.ForeColor = TXT; p.Controls.Add(c); }
    Button Mk(string t, int x, int y, int w, bool primary)
    {
        var b = new Button { Text = t, Location = new Point(x, y), Size = new Size(w, 30), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
        b.BackColor = primary ? ACC : SURF; b.ForeColor = primary ? Color.FromArgb(0, 19, 26) : TXT; b.FlatAppearance.BorderColor = primary ? ACC : LINE; return b;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var br = new LinearGradientBrush(new Rectangle(0, 0, Width, 76), Color.FromArgb(0, 70, 100), BG, 0f)) g.FillRectangle(br, 0, 0, Width, 76);
        using (var f = new Font("Segoe UI", 22f, FontStyle.Bold)) using (var b = new SolidBrush(ACC)) g.DrawString("GGSTUDIOS", f, b, 22, 8);
        using (var f = new Font("Segoe UI", 8.5f)) using (var b = new SolidBrush(DIM)) g.DrawString("FILE SHARE SETUP", f, b, 26, 50);
        using (var pen = new Pen(LINE)) g.DrawLine(pen, 0, 398, Width, 398);
    }

    void GoPage(int i)
    {
        cur = i;
        for (int k = 0; k < pages.Length; k++) pages[k].Visible = k == i;
        btnBack.Enabled = i == 1 || i == 2; btnBack.Visible = i < 4;
        btnNext.Text = i == 2 ? "Install" : i == 4 ? "Finish" : "Next >"; btnNext.Enabled = i != 3;
        btnCancel.Visible = i < 3;
    }

    void Next()
    {
        if (cur == 0) GoPage(1);
        else if (cur == 1)
        {
            string d = txtDir.Text.Trim();
            if (d == "" || !Path.IsPathRooted(d) || d.Length < 4) { MessageBox.Show("Please enter a full folder path, for example C:\\Program Files\\GGSTUDIOS\\GGShare"); return; }
            GoPage(2);
        }
        else if (cur == 2) StartInstall();
        else if (cur == 4)
        {
            if (success && chkLaunch.Checked && installedExe != "") { try { Process.Start("explorer.exe", "\"" + installedExe + "\""); } catch { } }   // via Explorer so the app runs un-elevated
            Close();
        }
    }

    void Log(string m) { if (IsDisposed) return; if (InvokeRequired) { try { BeginInvoke(new Action<string>(Log), m); } catch { } return; } txtLog.AppendText(m + "\r\n"); }

    void StartInstall()
    {
        GoPage(3); installing = true;
        string dir = txtDir.Text.Trim().TrimEnd('\\');
        bool desk = chkDesk.Checked, menu = chkMenu.Checked, auto = chkAuto.Checked, node = chkNode.Enabled && chkNode.Checked, ff = chkFfmpeg.Enabled && chkFfmpeg.Checked, ng = chkNgrok.Checked;
        var t = new Thread(() => DoInstall(dir, desk, menu, auto, node, ff, ng)); t.IsBackground = true; t.Start();
    }

    void WinGet(string args)
    {
        Log("> winget " + args);
        int code = Util.Run("winget", args + " --silent --accept-package-agreements --accept-source-agreements", delegate(string l) { Log("   " + l); });
        Log(code == 0 ? "   OK" : "   finished with code " + code + " (it may already be up to date, or winget is missing)");
    }

    void DoInstall(string dir, bool desk, bool menu, bool auto, bool node, bool ff, bool ng)
    {
        string problem = null;
        try
        {
            Log("Closing " + Info.Name + " if it is running...");
            Util.KillApp();

            Log("Copying files to " + dir);
            Directory.CreateDirectory(dir);
            string exe = Path.Combine(dir, Info.AppExe);
            for (int attempt = 1; ; attempt++)
            {
                try { using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(Info.AppExe)) using (var f = File.Create(exe)) s.CopyTo(f); break; }
                catch (IOException) { if (attempt >= 6) throw; Thread.Sleep(800); }
            }
            string un = Path.Combine(dir, "Uninstall.exe");
            if (!string.Equals(Path.GetFullPath(Application.ExecutablePath), Path.GetFullPath(un), StringComparison.OrdinalIgnoreCase)) File.Copy(Application.ExecutablePath, un, true);

            Log("Registering with Windows (Apps & features)...");
            using (var k = Registry.LocalMachine.CreateSubKey(Info.UninstKey))
            {
                k.SetValue("DisplayName", Info.Name); k.SetValue("DisplayVersion", Info.Version); k.SetValue("Publisher", Info.Publisher);
                k.SetValue("InstallLocation", dir); k.SetValue("DisplayIcon", exe); k.SetValue("UninstallString", "\"" + un + "\" /uninstall");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord); k.SetValue("NoRepair", 1, RegistryValueKind.DWord); k.SetValue("EstimatedSize", 600, RegistryValueKind.DWord);
            }

            try
            {
                if (desk) { Util.MakeShortcut(Util.DesktopLnk, exe, dir, Info.Name); Log("Desktop shortcut created."); }
                if (menu)
                {
                    Directory.CreateDirectory(Util.StartMenuDir);
                    Util.MakeShortcut(Path.Combine(Util.StartMenuDir, Info.Name + ".lnk"), exe, dir, Info.Name);
                    Util.MakeShortcut(Path.Combine(Util.StartMenuDir, "Uninstall " + Info.Name + ".lnk"), un, dir, "Remove " + Info.Name);
                    Log("Start menu shortcuts created.");
                }
            }
            catch (Exception ex) { Log("Could not create a shortcut: " + ex.Message); }

            using (var rk = Registry.CurrentUser.CreateSubKey(Info.RunKey))
            {
                if (auto) { rk.SetValue("GGShare", "\"" + exe + "\" --tray"); Log("Will start automatically with Windows (hidden in the tray)."); }
                else rk.DeleteValue("GGShare", false);
            }
            installedExe = exe;

            if (node) WinGet("install -e --id OpenJS.NodeJS.LTS");
            if (ff) WinGet("install -e --id ffmpeg");
            if (ng) WinGet(haveNgrok ? "upgrade -e --id ngrok.ngrok" : "install -e --id ngrok.ngrok");
        }
        catch (UnauthorizedAccessException) { problem = "Windows would not let Setup write to that folder. Run Setup again and choose a different folder, or right-click Setup and choose Run as administrator."; }
        catch (Exception ex) { problem = ex.Message; }

        try { BeginInvoke(new Action(() => Finished(problem))); } catch { }
    }

    void Finished(string problem)
    {
        installing = false; success = problem == null; bar.Visible = false;
        if (success)
        {
            lblResult.Text = Info.Name + " is installed.\r\n\r\nNext: open it, click Add to choose the folders to share, set a Viewer password, and use the gear button next to Internet to set your fixed link. Then tick both startup boxes so it runs by itself.";
            chkLaunch.Visible = true;
        }
        else { lblResult.Text = "Setup could not finish.\r\n\r\n" + problem; chkLaunch.Visible = false; chkLaunch.Checked = false; }
        GoPage(4);
    }
}

static class Uninstaller
{
    public static void Run(string[] a)
    {
        string dir = null;
        try { using (var k = Registry.LocalMachine.OpenSubKey(Info.UninstKey)) { if (k != null && k.GetValue("InstallLocation") != null) dir = k.GetValue("InstallLocation").ToString(); } } catch { }
        if (dir == null || !Directory.Exists(dir)) { MessageBox.Show(Info.Name + " does not appear to be installed.", Info.Name); return; }

        string self = Application.ExecutablePath;
        if (Array.IndexOf(a, "/stage2") < 0)
        {
            // first stage: copy ourselves to TEMP so this file in the install folder can be deleted
            string tmp = Path.Combine(Path.GetTempPath(), "GGShare-uninstall-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".exe");
            File.Copy(self, tmp, true);
            Process.Start(new ProcessStartInfo(tmp, "/uninstall /stage2") { UseShellExecute = true });
            return;
        }

        Thread.Sleep(1500);
        if (MessageBox.Show("Remove " + Info.Name + " from this computer?", "Uninstall " + Info.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) { SelfDelete(self); return; }
        bool wipe = MessageBox.Show("Also delete your saved settings (shared folders, passwords and link token)?\r\n\r\nChoose No to keep them in case you install again.", "Uninstall " + Info.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

        Util.KillApp();
        try { using (var rk = Registry.CurrentUser.OpenSubKey(Info.RunKey, true)) { if (rk != null) rk.DeleteValue("GGShare", false); } } catch { }
        try { Registry.LocalMachine.DeleteSubKeyTree(Info.UninstKey, false); } catch { }
        try { if (File.Exists(Util.DesktopLnk)) File.Delete(Util.DesktopLnk); } catch { }
        try { if (Directory.Exists(Util.StartMenuDir)) Directory.Delete(Util.StartMenuDir, true); } catch { }

        bool safe = File.Exists(Path.Combine(dir, Info.AppExe)) || File.Exists(Path.Combine(dir, "Uninstall.exe"));
        string removeErr = null;
        if (safe && dir.Length > 12)
        {
            for (int i = 0; i < 6; i++)
            {
                try { Directory.Delete(dir, true); removeErr = null; break; }
                catch (Exception ex) { removeErr = ex.Message; Thread.Sleep(1000); }
            }
        }
        if (wipe) { try { string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GGShare"); if (Directory.Exists(d)) Directory.Delete(d, true); } catch { } }

        MessageBox.Show(removeErr == null ? Info.Name + " has been removed." : Info.Name + " was removed, but some files could not be deleted:\r\n" + dir, Info.Name);
        SelfDelete(self);
    }

    static void SelfDelete(string file)
    {
        try { Process.Start(new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 > nul & del /f /q \"" + file + "\"") { CreateNoWindow = true, UseShellExecute = false }); } catch { }
    }
}
