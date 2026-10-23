using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using BetterSpeech.Patcher;

namespace BetterSpeech.Installer;

internal sealed class InstallerForm : Form
{
    private static readonly string PowerShellExe =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\WindowsPowerShell\v1.0\powershell.exe");

    private readonly TextBox pathBox = new TextBox();
    private readonly Button browseButton = new Button();
    private readonly Label statusLabel = new Label();
    private readonly Button installButton = new Button();
    private readonly Button uninstallButton = new Button();
    private readonly Button openButton = new Button();
    private readonly Button launchButton = new Button();
    private readonly TextBox logBox = new TextBox();
    private bool busy;

    public InstallerForm()
    {
        Text = $"Better Speech Recognition {Payload.Version} - Setup";
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(640, 470);
        MinimumSize = new Size(560, 420);
        try
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch (Exception)
        {
        }

        var title = new Label
        {
            Text = "Better Speech Recognition for Scam With Your Friends",
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(16, 14),
        };
        var pathLabel = new Label { Text = "Game folder", AutoSize = true, Location = new Point(16, 54) };

        pathBox.SetBounds(16, 76, 510, 26);
        pathBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        pathBox.TextChanged += (s, e) => UpdateStatus();

        Setup(browseButton, "Browse...", 534, 74, 90, 28, AnchorStyles.Top | AnchorStyles.Right, Browse);

        statusLabel.AutoSize = true;
        statusLabel.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        statusLabel.Location = new Point(16, 112);

        Setup(installButton, "Install", 16, 144, 140, 36, AnchorStyles.Top | AnchorStyles.Left, Install);
        Setup(uninstallButton, "Uninstall", 164, 144, 110, 36, AnchorStyles.Top | AnchorStyles.Left, Uninstall);
        Setup(openButton, "Open game folder", 282, 144, 140, 36, AnchorStyles.Top | AnchorStyles.Left, OpenFolder);
        Setup(launchButton, "Copy Steam launch option", 430, 144, 194, 36, AnchorStyles.Top | AnchorStyles.Right, CopyLaunchOption);

        var hint = new Label
        {
            Text = "Close the game before installing. After a game update, run this setup again and click Install, " +
                   "or paste the launch option into Steam (Properties > General > Launch options) to do it on every start.",
            Location = new Point(16, 188),
            Size = new Size(608, 40),
            ForeColor = Color.DimGray,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };

        logBox.Multiline = true;
        logBox.ReadOnly = true;
        logBox.ScrollBars = ScrollBars.Vertical;
        logBox.Font = new Font("Consolas", 9f);
        logBox.BackColor = Color.White;
        logBox.SetBounds(16, 234, 608, 220);
        logBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

        Controls.AddRange(new Control[]
        {
            title, pathLabel, pathBox, browseButton, statusLabel, installButton, uninstallButton, openButton, launchButton, hint, logBox,
        });

        Shown += (s, e) => FindGame();
    }

    private void Setup(Button button, string text, int x, int y, int width, int height, AnchorStyles anchor, Action click)
    {
        button.Text = text;
        button.SetBounds(x, y, width, height);
        button.Anchor = anchor;
        button.Click += (s, e) => click();
    }

    private string GameDir => pathBox.Text.Trim().TrimEnd('\\');

    private string ModDir => Path.Combine(GameDir, "BetterSpeech");

    private void FindGame()
    {
        if (!Payload.Available)
        {
            Log("This setup was built without the mod files. Build it with build.ps1 -Exe.");
        }
        string found = SteamLocator.FindGameDir();
        if (found != null)
        {
            pathBox.Text = found;
            Log("Found the game: " + found);
        }
        else
        {
            Log($"Could not find the game in your Steam libraries. Click Browse... and pick the folder with '{SteamLocator.GameExe}'.");
            UpdateStatus();
        }
    }

    private void UpdateStatus()
    {
        bool found = SteamLocator.IsGameDir(GameDir);
        bool installed = false;
        if (!found)
        {
            SetStatus("Game not found. Pick the game folder.", Color.Firebrick);
        }
        else
        {
            string status;
            try
            {
                status = PatchStatus(GameDir);
            }
            catch (Exception ex)
            {
                status = "error: " + ex.Message;
            }
            installed = status == "patched" || status == "outdated";
            switch (status)
            {
                case "patched":
                    SetStatus("Installed - start the game and press F8", Color.ForestGreen);
                    break;
                case "outdated":
                    SetStatus("Game was updated - click Install again", Color.DarkOrange);
                    break;
                case "not patched":
                    SetStatus("Not installed", Color.DimGray);
                    break;
                default:
                    SetStatus(status, Color.Firebrick);
                    break;
            }
        }
        installButton.Text = installed ? "Reinstall" : "Install";
        installButton.Enabled = found && !busy && Payload.Available;
        uninstallButton.Enabled = found && !busy && installed;
        openButton.Enabled = found && !busy;
        launchButton.Enabled = found && !busy;
        browseButton.Enabled = !busy;
        pathBox.ReadOnly = busy;
    }

    /// <summary>Separate method so a missing patcher assembly surfaces as a catchable exception.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static string PatchStatus(string gameDir) => GamePatcher.Status(gameDir);

    private void SetStatus(string text, Color color)
    {
        statusLabel.Text = text;
        statusLabel.ForeColor = color;
    }

    private void Log(string line)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(Log), line);
            return;
        }
        logBox.AppendText(line + Environment.NewLine);
    }

    private static bool GameRunning() => Process.GetProcessesByName(Path.GetFileNameWithoutExtension(SteamLocator.GameExe)).Length > 0;

    private bool ConfirmGameClosed()
    {
        if (!GameRunning())
        {
            return true;
        }
        MessageBox.Show(this, "The game is running. Close it first.", "Better Speech Recognition", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }

    /// <summary>Runs the step off the UI thread with the patcher's console output in the log.</summary>
    private async void RunStep(Action<string> step, string done)
    {
        if (busy || !ConfirmGameClosed())
        {
            return;
        }
        busy = true;
        UseWaitCursor = true;
        logBox.Clear();
        UpdateStatus();
        string gameDir = GameDir;
        TextWriter previousOut = Console.Out;
        Console.SetOut(new LogWriter(Log));
        try
        {
            await Task.Run(() => step(gameDir));
            Log("");
            Log(done);
        }
        catch (UnauthorizedAccessException ex)
        {
            Log("Error: " + ex.Message);
            Log("Windows did not allow writing to the game folder. Right-click the setup and choose 'Run as administrator'.");
        }
        catch (Exception ex)
        {
            Log("Error: " + ex.Message);
            Log("If the game no longer starts: Steam > Properties > Installed Files > Verify integrity of game files.");
        }
        finally
        {
            Console.SetOut(previousOut);
            busy = false;
            UseWaitCursor = false;
            UpdateStatus();
        }
    }

    private void Install()
    {
        RunStep(gameDir =>
        {
            string modDir = Path.Combine(gameDir, "BetterSpeech");
            Payload.ExtractTo(modDir, Log);
            GamePatcher.Install(gameDir, Path.Combine(modDir, "lib", "BetterSpeech.Runtime.dll"));
        }, "Done. Start the game and press F8 to pick and download a model.");
    }

    private void Uninstall()
    {
        DialogResult answer = MessageBox.Show(this, "Restore the original game files? Downloaded models and settings are kept.",
            "Better Speech Recognition", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes)
        {
            return;
        }
        RunStep(gameDir => GamePatcher.Uninstall(gameDir), "Done. The game files are original again.");
    }

    private void Browse()
    {
        using (var dialog = new FolderBrowserDialog { Description = $"Select the folder that contains '{SteamLocator.GameExe}'" })
        {
            if (SteamLocator.IsGameDir(GameDir))
            {
                dialog.SelectedPath = GameDir;
            }
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }
            pathBox.Text = dialog.SelectedPath;
            if (!SteamLocator.IsGameDir(dialog.SelectedPath))
            {
                Log($"'{SteamLocator.GameExe}' is not in {dialog.SelectedPath}.");
            }
        }
    }

    private void OpenFolder() => Process.Start("explorer.exe", "\"" + GameDir + "\"");

    private void CopyLaunchOption()
    {
        string launch = Path.Combine(ModDir, "launch.ps1");
        string option = $"\"{PowerShellExe}\" -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{launch}\" %command%";
        Clipboard.SetText(option);
        Log("Copied to the clipboard. In Steam: right-click the game > Properties > General > Launch options, paste it there:");
        Log(option);
        Log("It reinstalls the mod before each start if a game update removed it. Click Install here once first.");
    }

    /// <summary>Forwards Console output (GamePatcher writes there) to the log, one line at a time.</summary>
    private sealed class LogWriter : TextWriter
    {
        private readonly Action<string> log;
        private readonly StringBuilder line = new StringBuilder();

        public LogWriter(Action<string> log) => this.log = log;

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            lock (line)
            {
                if (value == '\n')
                {
                    log(line.ToString().TrimEnd('\r'));
                    line.Clear();
                }
                else
                {
                    line.Append(value);
                }
            }
        }

        public override void WriteLine(string value)
        {
            lock (line)
            {
                line.Append(value);
                log(line.ToString());
                line.Clear();
            }
        }
    }
}
