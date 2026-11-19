// Pale Knight Cataclysm: the windowed installer for the stand-alone (Nexus) download.
// Built with the C# compiler every Windows PC has (.NET Framework 4, C# 5), so players need nothing extra.
// It only finds the two game folders and runs LopArsenal\LopArsenal.exe, which builds the mod from the
// player's own Lies of P and Bloodborne; it shows that program's stages as a progress bar.
// GPL-3.0, like the rest of the mod.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

static class InstallerProgram
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new InstallerWindow(Array.IndexOf(args, "/auto") >= 0));
    }
}

class InstallerWindow : Form
{
    readonly string tool;
    readonly TextBox bbBox = new TextBox(), lopBox = new TextBox(), logBox = new TextBox();
    readonly Label status = new Label();
    readonly ProgressBar bar = new ProgressBar();
    readonly Button install = new Button(), close = new Button(), bbBrowse = new Button(), lopBrowse = new Button();
    Process proc;

    readonly bool auto; // /auto: press Install by itself (testing)
    public InstallerWindow(bool auto)
    {
        this.auto = auto;
        tool = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LopArsenal", "LopArsenal.exe");
        Text = "Pale Knight Cataclysm installer";
        ClientSize = new Size(640, 430);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);

        var title = new Label { Text = "Pale Knight Cataclysm", Font = new Font("Segoe UI", 14f, FontStyle.Bold), Location = new Point(16, 12), AutoSize = true };
        var intro = new Label
        {
            Text = "Builds Lies of P's Pale Knight and Cataclysm into Bloodborne (bbport-windows) from your own copies of both games. Close Bloodborne first.",
            Location = new Point(16, 44), Size = new Size(608, 36)
        };
        Controls.Add(title); Controls.Add(intro);
        AddFolderRow("Bloodborne (bbport-windows) folder:", bbBox, bbBrowse, 86);
        AddFolderRow("Lies of P folder:", lopBox, lopBrowse, 140);

        status.Location = new Point(16, 196); status.Size = new Size(608, 20); status.Text = "Looking for your games...";
        bar.Location = new Point(16, 218); bar.Size = new Size(608, 22); bar.Maximum = 100;
        logBox.Location = new Point(16, 248); logBox.Size = new Size(608, 128); logBox.Multiline = true;
        logBox.ReadOnly = true; logBox.ScrollBars = ScrollBars.Vertical; logBox.Font = new Font("Consolas", 8f);
        install.Text = "Install"; install.Location = new Point(428, 388); install.Size = new Size(96, 30);
        close.Text = "Close"; close.Location = new Point(528, 388); close.Size = new Size(96, 30);
        install.Click += delegate { Start(); };
        close.Click += delegate { Close(); };
        Controls.Add(status); Controls.Add(bar); Controls.Add(logBox); Controls.Add(install); Controls.Add(close);
        AcceptButton = install;
        Shown += delegate { FindGames(); if (this.auto && install.Enabled) Start(); };
        FormClosing += (s, e) =>
        {
            if (proc != null && !proc.HasExited &&
                MessageBox.Show(this, "The installer is still building. Stop it?", Text, MessageBoxButtons.YesNo) == DialogResult.No)
                e.Cancel = true;
            else if (proc != null && !proc.HasExited) { try { proc.Kill(); } catch { } }
        };
    }

    void AddFolderRow(string label, TextBox box, Button browse, int y)
    {
        Controls.Add(new Label { Text = label, Location = new Point(16, y), AutoSize = true });
        box.Location = new Point(16, y + 20); box.Size = new Size(512, 24);
        browse.Text = "Browse..."; browse.Location = new Point(536, y + 18); browse.Size = new Size(88, 27);
        browse.Click += delegate
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = label;
                if (Directory.Exists(box.Text)) dlg.SelectedPath = box.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK) box.Text = dlg.SelectedPath;
            }
        };
        Controls.Add(box); Controls.Add(browse);
    }

    // The bbport-windows folder: it holds CUSA03173 itself, or it is the port (Play Bloodborne.exe) and the
    // launcher's game folder setting points to the game files elsewhere (LopArsenal.exe finds them from there).
    static bool IsBb(string d)
    {
        if (d.Length == 0) return false;
        try
        {
            return Directory.Exists(Path.Combine(d, "CUSA03173", "dvdroot_ps4"))
                || File.Exists(Path.Combine(d, "Play Bloodborne.exe")) || File.Exists(Path.Combine(d, "Bloodborne.exe"));
        }
        catch { return false; }
    }
    static bool IsLop(string d) { return d.Length > 0 && Directory.Exists(Path.Combine(d, "LiesofP", "Content", "Paks")); }

    void FindGames()
    {
        if (!File.Exists(tool))
        {
            status.Text = "LopArsenal\\LopArsenal.exe is missing: unzip the whole download, keeping the LopArsenal folder next to this file.";
            install.Enabled = false;
            return;
        }
        try
        {
            var psi = Info("find");
            using (var p = Process.Start(psi))
            {
                string line;
                while ((line = p.StandardOutput.ReadLine()) != null)
                {
                    if (line.StartsWith("bb=")) bbBox.Text = line.Substring(3);
                    if (line.StartsWith("lop=")) lopBox.Text = line.Substring(4);
                }
                p.WaitForExit();
            }
        }
        catch (Exception e) { Log("Couldn't look for the games: " + e.Message); }
        status.Text = IsBb(bbBox.Text) && IsLop(lopBox.Text)
            ? "Found both games. Press Install."
            : "Pick the folder the installer couldn't find (Browse...), then press Install.";
    }

    ProcessStartInfo Info(string args)
    {
        return new ProcessStartInfo(tool, args)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            RedirectStandardInput = true, WorkingDirectory = Path.GetDirectoryName(tool)
        };
    }

    void Start()
    {
        string bb = bbBox.Text.Trim().Trim('"'), lop = lopBox.Text.Trim().Trim('"');
        if (!IsBb(bb)) { MessageBox.Show(this, "That isn't the bbport-windows folder: pick the folder that holds Play Bloodborne.exe.", Text); return; }
        if (!IsLop(lop)) { MessageBox.Show(this, "That isn't the Lies of P folder: pick the folder that holds LiesofP.", Text); return; }
        install.Enabled = bbBrowse.Enabled = lopBrowse.Enabled = false;
        bbBox.ReadOnly = lopBox.ReadOnly = true;
        logBox.Clear();
        bar.Style = ProgressBarStyle.Marquee;
        status.Text = "Starting (this takes a few minutes)...";
        proc = new Process { StartInfo = Info("install \"" + lop + "\" \"" + bb + "\""), EnableRaisingEvents = true };
        proc.OutputDataReceived += (s, e) => { if (e.Data != null) BeginInvoke(new Action<string>(OnLine), e.Data); };
        proc.ErrorDataReceived += (s, e) => { if (e.Data != null) BeginInvoke(new Action<string>(Log), e.Data); };
        proc.Exited += delegate { BeginInvoke(new Action(Finished)); };
        try
        {
            proc.Start();
            proc.StandardInput.Close();
            proc.BeginOutputReadLine(); proc.BeginErrorReadLine();
        }
        catch (Exception e) { Log("Couldn't start the build: " + e.Message); Finished(); }
    }

    void OnLine(string line)
    {
        if (line.StartsWith("@@stage "))
        {
            var rest = line.Substring(8);
            int sp = rest.IndexOf(' ');
            status.Text = (sp > 0 ? rest.Substring(sp + 1) : rest) + "...";
            return;
        }
        if (line.StartsWith("@@progress "))
        {
            double v;
            var num = line.Substring(11).Split(' ')[0];
            if (double.TryParse(num, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v))
            {
                bar.Style = ProgressBarStyle.Continuous;
                bar.Value = Math.Max(bar.Value, Math.Min(100, (int)(v * 100)));
            }
            return;
        }
        Log(line);
    }

    void Log(string line) { logBox.AppendText(line + Environment.NewLine); }

    void Finished()
    {
        int code = -1;
        try { if (proc != null && proc.HasExited) code = proc.ExitCode; } catch { }
        bar.Style = ProgressBarStyle.Continuous;
        if (code == 0)
        {
            bar.Value = 100;
            status.Text = "Done! Open the bbport launcher, make sure \"Lies of P Arsenal\" is enabled, and play.";
            install.Text = "Rebuild";
            if (!auto) MessageBox.Show(this, "Pale Knight Cataclysm is installed.\n\nOpen the bbport launcher, make sure \"Lies of P Arsenal\" is enabled in Mods, and play. Buy the weapons from the Messengers' bath shop in the Hunter's Dream.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            bar.Value = 0;
            status.Text = code == 4
                ? "Couldn't find Bloodborne's game files: open Play Bloodborne.exe once, choose your game folder there, then press Install again."
                : "Something went wrong: see the messages below. Close Bloodborne and try again.";
        }
        install.Enabled = bbBrowse.Enabled = lopBrowse.Enabled = true;
        bbBox.ReadOnly = lopBox.ReadOnly = false;
        proc = null;
    }
}
