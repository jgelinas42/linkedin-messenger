using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        using var mutex = new Mutex(true, "LinkedInMessenger.Single", out bool first);
        var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "LinkedInMessenger.Show");
        if (!first) { showSignal.Set(); return; } // 2e lancement : réveille l'instance existante

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(showSignal, startHidden: args.Contains("--tray")));
    }
}

class MainForm : Form
{
    const string Home = "https://www.linkedin.com/messaging/";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string AppName = "LinkedIn Messenger";
    static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LinkedInMessenger");
    static readonly string BoundsFile = Path.Combine(DataDir, "window.txt");
    static readonly string IconFile = Path.Combine(AppContext.BaseDirectory, "linkedin.ico");
    // Hôtes ouverts dans l'app ; tout le reste part dans le navigateur par défaut.
    static readonly string[] InAppHosts = { "linkedin.com", "licdn.com", "accounts.google.com", "appleid.apple.com", "login.microsoftonline.com", "login.live.com" };

    readonly WebView2 web = new() { Dock = DockStyle.Fill };
    readonly NotifyIcon tray = new() { Icon = new Icon(IconFile, SystemInformation.SmallIconSize), Text = AppName, Visible = true };
    bool quitting;
    int unread;
    CoreWebView2Notification pendingClick; // notification Web liée à la dernière bulle affichée
    DateTime lastWebNotif;

    public MainForm(EventWaitHandle showSignal, bool startHidden)
    {
        Text = AppName;
        Icon = new Icon(IconFile);
        Size = new Size(900, 650);
        MinimumSize = new Size(360, 400);
        Opacity = 0; // évite le flash au démarrage en --tray
        Controls.Add(web);
        LoadBounds();
        ResizeEnd += (_, _) => SaveBounds(); // à chaque déplacement/redimensionnement : survit à un arrêt brutal

        var autostart = new ToolStripMenuItem("Démarrer avec Windows") { Checked = Registry.CurrentUser.OpenSubKey(RunKey)?.GetValue("LinkedInMessenger") != null };
        autostart.Click += (_, _) =>
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (autostart.Checked) key.DeleteValue("LinkedInMessenger", false);
            else key.SetValue("LinkedInMessenger", $"\"{Environment.ProcessPath}\" --tray");
            autostart.Checked = !autostart.Checked;
        };
        tray.ContextMenuStrip = new ContextMenuStrip();
        tray.ContextMenuStrip.Items.Add("Ouvrir", null, (_, _) => ShowWindow());
        tray.ContextMenuStrip.Items.Add("Retour à la messagerie", null, (_, _) => { ShowWindow(); web.CoreWebView2?.Navigate(Home); });
        tray.ContextMenuStrip.Items.Add(autostart);
        tray.ContextMenuStrip.Items.Add("Quitter", null, (_, _) => { quitting = true; Close(); });
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowWindow(); };
        tray.BalloonTipClicked += (_, _) => { ShowWindow(); pendingClick?.ReportClicked(); };

        ThreadPool.RegisterWaitForSingleObject(showSignal, (_, _) => BeginInvoke((Action)ShowWindow), null, -1, false);
        Shown += async (_, _) => { if (startHidden) Hide(); Opacity = 1; await InitWebView(); };
    }

    async Task InitWebView()
    {
        // Sans ces options, Chromium ralentit la page cachée et les messages arrivent en retard.
        var env = await CoreWebView2Environment.CreateAsync(null, DataDir, new CoreWebView2EnvironmentOptions(
            "--disable-background-timer-throttling --disable-renderer-backgrounding --disable-backgrounding-occluded-windows"));
        await web.EnsureCoreWebView2Async(env);
        var core = web.CoreWebView2;

        var css = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "compact.css"));
        await core.AddScriptToExecuteOnDocumentCreatedAsync(
            $"document.addEventListener('DOMContentLoaded',()=>{{const s=document.createElement('style');s.textContent={JsonSerializer.Serialize(css)};document.head.appendChild(s);}});");

        core.PermissionRequested += (_, e) =>
        {
            if (e.PermissionKind == CoreWebView2PermissionKind.Notifications) e.State = CoreWebView2PermissionState.Allow;
        };
        // Voie 1 : LinkedIn émet une notification Web → bulle Windows, le clic ouvre la conversation.
        core.NotificationReceived += (_, e) =>
        {
            e.Handled = true;
            e.Notification.ReportShown();
            lastWebNotif = DateTime.Now;
            Notify(e.Notification.Title, e.Notification.Body, e.Notification);
        };
        // Voie 2 (filet) : le compteur « (N) » du titre de l'onglet augmente.
        core.DocumentTitleChanged += (_, _) =>
        {
            var m = Regex.Match(core.DocumentTitle, @"^\((\d+)\)");
            int count = m.Success ? int.Parse(m.Groups[1].Value) : 0;
            if (count > unread && DateTime.Now - lastWebNotif > TimeSpan.FromSeconds(10))
                Notify("LinkedIn", $"{count} élément(s) non lu(s)", null);
            unread = count;
            Text = tray.Text = count > 0 ? $"({count}) {AppName}" : AppName;
        };
        core.NewWindowRequested += (_, e) =>
        {
            if (!InApp(e.Uri)) { e.Handled = true; OpenExternal(e.Uri); }
        };
        core.NavigationStarting += (_, e) =>
        {
            var u = new Uri(e.Uri);
            if (u.Host.EndsWith("linkedin.com") && (u.AbsolutePath == "/" || u.AbsolutePath.StartsWith("/feed")))
            { e.Cancel = true; core.Navigate(Home); } // pas de fil d'actualité, y compris après la connexion
            else if (!InApp(e.Uri)) { e.Cancel = true; OpenExternal(e.Uri); }
        };
        core.Navigate(Home);
    }

    void Notify(string title, string body, CoreWebView2Notification source)
    {
        if (ActiveForm == this) return; // comme Telegram : pas de bulle si la fenêtre est au premier plan
        pendingClick = source;
        tray.ShowBalloonTip(5000, title, string.IsNullOrEmpty(body) ? " " : body, ToolTipIcon.None);
    }

    void ShowWindow()
    {
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!quitting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; } // X = zone de notification
        SaveBounds();
        tray.Dispose();
        base.OnFormClosing(e);
    }

    static bool InApp(string uri) =>
        !Uri.TryCreate(uri, UriKind.Absolute, out var u) || (u.Scheme != "http" && u.Scheme != "https")
        || InAppHosts.Any(h => u.Host == h || u.Host.EndsWith("." + h));

    static void OpenExternal(string uri) => Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });

    void LoadBounds()
    {
        try
        {
            var p = File.ReadAllText(BoundsFile).Split(',').Select(int.Parse).ToArray();
            var r = new Rectangle(p[0], p[1], p[2], p[3]);
            if (Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(r))) { StartPosition = FormStartPosition.Manual; Bounds = r; }
        }
        catch { } // ponytail: fichier absent ou corrompu → taille par défaut
    }

    void SaveBounds()
    {
        var r = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        Directory.CreateDirectory(DataDir);
        File.WriteAllText(BoundsFile, $"{r.X},{r.Y},{r.Width},{r.Height}");
    }
}
