// CosmOrganizer - Main Application & Form
// WinForms + WebView2 host for modern HTML interface.
// © CosmoNet.info — MIT License

using System;
using System.IO;
using System.Windows.Forms;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Media;
using System.Diagnostics;
using Microsoft.Win32;
using System.Text.Json;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Velopack;
using CosmOrganizer.Core;

namespace CosmOrganizer
{
    public class CosmOrganizerForm : Form
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        // WebView & UI
        private WebView2? _webView;
        private NotifyIcon? _trayIcon;
        private System.Windows.Forms.Timer? _scheduleTimer;
        private UpdateService? _updateService;

        // Core Services
        private readonly SafeFileService _safeFile = new();
        private readonly SettingsService _settingsService = new();
        private readonly JournalService _journal = new();
        private readonly CategoryService _categoryService = new();
        private readonly ProfileService _profileService = new();
        private readonly RuleEngine _ruleEngine = new();
        private readonly SchedulerService _scheduler = new();
        private readonly OrganizationService _orgService;
        private readonly DuplicateFinderService _duplicateService;
        private readonly StorageAnalyzerService _storageService;
        private readonly ArchiveService _archiveService;
        private readonly InstallerCleanerService _installerService;
        private readonly MediaSorterService _mediaService;
        private readonly BatchRenamerService _renamerService;
        private readonly JournalSearchService _journalSearchService;
        private readonly MaintenanceService _maintenanceService;
        private readonly FolderHealthService _healthService;
        private RealTimeService? _realTime;

        // State
        private CosmOrganizer.Core.AppSettings _coreSettings = new();
        private List<Profile> _profiles = new();
        private Profile _activeProfile;
        private string _currentRunId = string.Empty;
        private bool _isRealTimeEnabled;
        private DateTime? _pauseUntil;
        private bool _showCorruptedWarning;
        private readonly List<object> _sessionMoved = new();
        private readonly List<string> _sessionDeleted = new();
        private CancellationTokenSource? _runCts;

        public CosmOrganizerForm(string[] args)
        {
            this.Text = "CosmOrganizer";
            this.Size = new Size(1200, 850);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MinimumSize = new Size(1000, 750);
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.BackColor = Color.FromArgb(5, 7, 10);

            // 1. Load Services & Data
            LoadCoreSettings();
            _categoryService.LoadFromAppDir();
            _profiles = _profileService.Load();
            _activeProfile = _profiles.FirstOrDefault(p => p.Enabled) ?? _profiles.First();
            _orgService = new OrganizationService(_safeFile, _categoryService, _ruleEngine, _journal);
            _duplicateService = new DuplicateFinderService(_safeFile, _journal);
            _storageService = new StorageAnalyzerService(_safeFile);
            _archiveService = new ArchiveService(_safeFile, _journal);
            _installerService = new InstallerCleanerService(_safeFile, _journal);
            _mediaService = new MediaSorterService(_safeFile, _journal);
            _renamerService = new BatchRenamerService(_safeFile, _journal);
            _journalSearchService = new JournalSearchService(_journal);
            _maintenanceService = new MaintenanceService(_safeFile, _journal);
            _healthService = new FolderHealthService(_categoryService, _safeFile);

            // 2. Setup Tray Icon
            InitializeTrayIcon();

            // 3. Setup Scheduler
            InitializeScheduler();

            // 4. Initialize WebView2 UI
            InitializeWebView(args);

            // 5. Velopack Auto-Update
            _updateService = new UpdateService(() => _coreSettings.AutoUpdateEnabled, OnUpdateReady);
            _updateService.Start();

            // 6. Handle CLI arguments
            ProcessCliArgs(args);

            this.FormClosing += OnFormClosing;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int darkMode = 1;
                DwmSetWindowAttribute(this.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
            }
            catch { }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Z))
            {
                ConfirmUndoFromKeyboard();
                return true;
            }
            if (keyData == (Keys.Control | Keys.O))
            {
                BrowseForSourceFolder();
                return true;
            }
            if (keyData == Keys.F1)
            {
                OpenHelp();
                return true;
            }
            if (keyData == Keys.F5)
            {
                UpdateUIStats();
                UpdateSourceUI();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void ConfirmUndoFromKeyboard()
        {
            var res = MessageBox.Show(
                "Vuoi annullare l'ultima operazione di spostamento file?\n\nI file verranno riportati nelle cartelle originali.",
                "Conferma Annulla (Ctrl+Z)",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (res == DialogResult.Yes)
            {
                UndoOperations();
            }
        }

        private void OpenHelp()
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = "https://www.cosmonet.info/scrivici/", UseShellExecute = true });
            }
            catch { }
        }

        // ---------------------------------------------------------------------------
        // Settings & Profiles
        // ---------------------------------------------------------------------------

        private void LoadCoreSettings()
        {
            try
            {
                var (s, corrupted) = _settingsService.Load();
                _coreSettings = s;
                _isRealTimeEnabled = false;
                if (corrupted) _showCorruptedWarning = true;
            }
            catch
            {
                _coreSettings = new();
            }
        }

        private void SaveCoreSettings()
        {
            try { _settingsService.Save(_coreSettings); } catch { }
        }

        private void SaveProfiles()
        {
            try { _profileService.Save(_profiles); } catch { }
        }

        // ---------------------------------------------------------------------------
        // Tray Icon & Background
        // ---------------------------------------------------------------------------

        private void InitializeTrayIcon()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Organizza ora", null, (s, e) => OrganizeActiveProfile());
            menu.Items.Add("Pausa 1 ora", null, (s, e) => PauseMonitoring(TimeSpan.FromHours(1)));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Apri CosmOrganizer", null, (s, e) => RestoreWindow());
            menu.Items.Add("Esci", null, (s, e) => ExitApplication());

            _trayIcon = new NotifyIcon
            {
                Text = "CosmOrganizer",
                Icon = this.Icon ?? SystemIcons.Application,
                ContextMenuStrip = menu,
                Visible = true
            };
            _trayIcon.DoubleClick += (s, e) => RestoreWindow();
        }

        private void RestoreWindow()
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.BringToFront();
            this.Activate();
        }

        private void PauseMonitoring(TimeSpan duration)
        {
            _pauseUntil = DateTime.Now.Add(duration);
            _realTime?.Pause();
            Log($"⏸️ Monitoraggio e pianificazione in pausa fino alle {_pauseUntil.Value:HH:mm}");
            UpdateUIStats();
        }

        private void ExitApplication()
        {
            _trayIcon?.Dispose();
            _trayIcon = null;
            Application.Exit();
        }

        // ---------------------------------------------------------------------------
        // Scheduler
        // ---------------------------------------------------------------------------

        private void InitializeScheduler()
        {
            _scheduler.ProfileDue += profileId =>
            {
                if (_pauseUntil.HasValue && DateTime.Now < _pauseUntil.Value) return;

                var profile = _profiles.FirstOrDefault(p => p.Id == profileId);
                if (profile != null)
                {
                    this.BeginInvoke(new Action(() => RunProfile(profile)));
                }
            };

            _scheduleTimer = new System.Windows.Forms.Timer { Interval = 60_000 };
            _scheduleTimer.Tick += (s, e) =>
            {
                if (_pauseUntil.HasValue && DateTime.Now >= _pauseUntil.Value)
                {
                    _pauseUntil = null;
                    _realTime?.Resume();
                    Log("▶️ Monitoraggio e pianificazione riattivati.");
                    UpdateUIStats();
                }

                if (_pauseUntil.HasValue && DateTime.Now < _pauseUntil.Value) return;

                _scheduler.Tick(_profiles);

                // Check folder thresholds
                foreach (var breached in _scheduler.GetThresholdBreached(_profiles))
                {
                    RunProfile(breached);
                    _scheduler.MarkRan(breached.Id);
                }
            };
            _scheduleTimer.Start();

            // Run on-startup profiles
            foreach (var startupProfile in _scheduler.GetStartupProfiles(_profiles))
            {
                Task.Run(() => RunProfile(startupProfile));
            }
        }

        // ---------------------------------------------------------------------------
        // CLI Arguments
        // ---------------------------------------------------------------------------

        private void ProcessCliArgs(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg.Equals("--background", StringComparison.OrdinalIgnoreCase))
                {
                    this.WindowState = FormWindowState.Minimized;
                    this.ShowInTaskbar = false;
                    this.Hide();
                }
                else if (arg.Equals("--profile", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    var pName = args[++i];
                    var match = _profiles.FirstOrDefault(p => p.Name.Equals(pName, StringComparison.OrdinalIgnoreCase));
                    if (match != null) _activeProfile = match;
                }
                else if (arg.Equals("--organize", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    var folder = args[++i];
                    if (Directory.Exists(folder))
                    {
                        if (!_activeProfile.SourceFolders.Contains(folder))
                            _activeProfile.SourceFolders.Add(folder);
                        SaveProfiles();
                        this.Load += (s, e) => OrganizeActiveProfile();
                    }
                }
                else if (arg.Equals("--analyze", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    var folder = args[++i];
                    if (Directory.Exists(folder))
                    {
                        this.Load += (s, e) =>
                        {
                            var res = _storageService.Analyze(folder);
                            PostMessage("storageData", res);
                            _webView?.ExecuteScriptAsync("showView('storage');");
                        };
                    }
                }
                else if (arg.Equals("--preview", StringComparison.OrdinalIgnoreCase))
                {
                    this.Load += (s, e) => RequestPreview();
                }
                else if (Directory.Exists(arg))
                {
                    // Dragged folder onto exe
                    if (!_activeProfile.SourceFolders.Contains(arg))
                        _activeProfile.SourceFolders.Add(arg);
                    SaveProfiles();
                }
            }
        }

        // ---------------------------------------------------------------------------
        // WebView2 Setup
        // ---------------------------------------------------------------------------

        private async void InitializeWebView(string[] args)
        {
            _webView = new WebView2 { Dock = DockStyle.Fill, AllowExternalDrop = true };
            Controls.Add(_webView);

            try
            {
                var userFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CosmOrganizer", "WebView2");
                Directory.CreateDirectory(userFolder);
                var env = await CoreWebView2Environment.CreateAsync(null, userFolder);
                await _webView.EnsureCoreWebView2Async(env);
            }
            catch (Exception)
            {
                Controls.Remove(_webView);
                _webView.Dispose();
                _webView = null;

                var pnl = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(5, 7, 10) };
                var lbl = new Label
                {
                    Text = "Per usare CosmOrganizer è necessario il runtime Microsoft Edge WebView2.\n\nIl componente non è presente o non è stato possibile avviarlo.",
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 11, FontStyle.Regular),
                    AutoSize = false,
                    Size = new Size(600, 70),
                    Location = new Point(50, 60),
                    TextAlign = ContentAlignment.TopLeft
                };
                var link = new LinkLabel
                {
                    Text = "Scarica Microsoft Edge WebView2 Runtime dal sito ufficiale Microsoft",
                    LinkColor = Color.FromArgb(88, 166, 255),
                    Font = new Font("Segoe UI", 10, FontStyle.Underline),
                    AutoSize = true,
                    Location = new Point(50, 140)
                };
                link.LinkClicked += (s, ev) =>
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo("https://developer.microsoft.com/microsoft-edge/webview2/") { UseShellExecute = true });
                    }
                    catch { }
                };
                pnl.Controls.Add(lbl);
                pnl.Controls.Add(link);
                Controls.Add(pnl);
                return;
            }

            string htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Interface", "index.html");
            if (!File.Exists(htmlPath))
            {
                MessageBox.Show($"Interfaccia HTML non trovata:\n{htmlPath}", "Errore", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _webView.Source = new Uri(htmlPath);
            _webView.WebMessageReceived += WebView_WebMessageReceived;
            _webView.NavigationCompleted += (s, e) =>
            {
                UpdateUIStats();
                var ver = typeof(CosmOrganizerForm).Assembly.GetName().Version;
                string verStr = ver != null ? $"{ver.Major}.{ver.Minor}" : "3.3";
                _webView.ExecuteScriptAsync($"setAppVersion('{verStr}');");
            };

            // Drag and Drop folder support
            _webView.DragEnter += (s, e) =>
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
            };
            _webView.DragDrop += (s, e) =>
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    foreach (var path in files)
                    {
                        if (Directory.Exists(path) && !_activeProfile.SourceFolders.Contains(path))
                        {
                            _activeProfile.SourceFolders.Add(path);
                            SaveProfiles();
                            Log($"Cartella aggiunta: {Path.GetFileName(path)}");
                        }
                    }
                    UpdateSourceUI();
                    UpdateWatchers();
                    try { SystemSounds.Asterisk.Play(); } catch { }
                }
            };
        }

        private void OnUpdateReady(string newVersion)
        {
            if (this.IsDisposed || !this.IsHandleCreated) return;
            this.BeginInvoke(new Action(() =>
            {
                _webView?.CoreWebView2?.ExecuteScriptAsync($"showUpdateBanner('{newVersion}');");
            }));
        }

        // ---------------------------------------------------------------------------
        // WebMessage Dispatcher (JSON-based)
        // ---------------------------------------------------------------------------

        private async void WebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var raw = e.TryGetWebMessageAsString();
                if (string.IsNullOrWhiteSpace(raw)) return;

                if (raw == "pageReady")
                {
                    LoadInitialData();
                }
                else if (raw == "organize")
                {
                    OrganizeActiveProfile();
                }
                else if (raw == "undo")
                {
                    UndoOperations();
                }
                else if (raw == "preview")
                {
                    RequestPreview();
                }
                else if (raw.StartsWith("applyPreview:"))
                {
                    var json = raw["applyPreview:".Length..];
                    ApplyPreviewSelected(json);
                }
                else if (raw == "toggleRealTime")
                {
                    ToggleRealTimeMonitoring();
                }
                else if (raw.StartsWith("set:activeProfile:"))
                {
                    var name = raw["set:activeProfile:".Length..];
                    SwitchActiveProfile(name);
                }
                else if (raw.StartsWith("set:organizeMode:"))
                {
                    var modeStr = raw["set:organizeMode:".Length..];
                    if (Enum.TryParse<OrganizeMode>(modeStr, true, out var mode))
                    {
                        _activeProfile.OrganizeMode = mode;
                        SaveProfiles();
                        Log($"Modalità impostata a: {mode}");
                    }
                }
                else if (raw.StartsWith("toggleRule:"))
                {
                    var parts = raw["toggleRule:".Length..].Split(':');
                    if (parts.Length == 2)
                    {
                        var rule = _activeProfile.Rules.FirstOrDefault(r => r.Id == parts[0]);
                        if (rule != null)
                        {
                            rule.Enabled = parts[1] == "true";
                            SaveProfiles();
                        }
                    }
                }
                else if (raw.StartsWith("testRule:"))
                {
                    var ruleId = raw["testRule:".Length..];
                    TestRuleOnFile(ruleId);
                }
                else if (raw == "addRule")
                {
                    AddNewRuleDialog();
                }
                else if (raw == "findDuplicates")
                {
                    Task.Run(() =>
                    {
                        var groups = _duplicateService.FindDuplicates(_activeProfile.SourceFolders);
                        _duplicateService.ApplyAutoSelection(groups, KeepStrategy.KeepOldest);
                        this.BeginInvoke(new Action(() => PostMessage("duplicatesData", groups)));
                    });
                }
                else if (raw.StartsWith("recycleDuplicates:"))
                {
                    var json = raw["recycleDuplicates:".Length..];
                    var groups = JsonSerializer.Deserialize<List<DuplicateGroup>>(json) ?? new();
                    _currentRunId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_") + Guid.NewGuid().ToString("N")[..8];
                    Task.Run(() =>
                    {
                        int recycled = _duplicateService.RecycleSelected(groups, _currentRunId);
                        this.BeginInvoke(new Action(() =>
                        {
                            Log($"🗑️ Spostati nel Cestino {recycled} duplicati.");
                            UpdateUIStats();
                        }));
                    });
                }
                else if (raw == "analyzeStorage")
                {
                    var folder = _activeProfile.SourceFolders.FirstOrDefault() ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    Task.Run(() =>
                    {
                        var result = _storageService.Analyze(folder);
                        this.BeginInvoke(new Action(() => PostMessage("storageData", result)));
                    });
                }
                else if (raw == "scanJunk")
                {
                    var folder = _activeProfile.SourceFolders.FirstOrDefault() ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    Task.Run(() =>
                    {
                        var candidates = _maintenanceService.ScanForJunk(folder);
                        this.BeginInvoke(new Action(() => PostMessage("junkData", candidates)));
                    });
                }
                else if (raw.StartsWith("recycleJunk:"))
                {
                    var json = raw["recycleJunk:".Length..];
                    var candidates = JsonSerializer.Deserialize<List<CleanupCandidate>>(json) ?? new();
                    _currentRunId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_") + Guid.NewGuid().ToString("N")[..8];
                    Task.Run(() =>
                    {
                        int count = _maintenanceService.RecycleJunk(candidates, _currentRunId);
                        this.BeginInvoke(new Action(() =>
                        {
                            Log($"🧹 Eliminati {count} file temporanei/vuoti.");
                            UpdateUIStats();
                        }));
                    });
                }
                else if (raw == "cleanInstallers")
                {
                    var folder = _activeProfile.SourceFolders.FirstOrDefault() ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    Task.Run(() =>
                    {
                        var items = _installerService.Analyze(folder);
                        this.BeginInvoke(new Action(() => PostMessage("installerData", items)));
                    });
                }
                else if (raw.StartsWith("recycleInstallers:"))
                {
                    var json = raw["recycleInstallers:".Length..];
                    var items = JsonSerializer.Deserialize<List<InstallerItem>>(json) ?? new();
                    _currentRunId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_") + Guid.NewGuid().ToString("N")[..8];
                    Task.Run(() =>
                    {
                        int count = _installerService.RecycleSelected(items, _currentRunId);
                        this.BeginInvoke(new Action(() =>
                        {
                            Log($"📦 Cestinati {count} vecchi installer.");
                            UpdateUIStats();
                        }));
                    });
                }
                else if (raw.StartsWith("searchJournal:"))
                {
                    var query = raw["searchJournal:".Length..];
                    Task.Run(() =>
                    {
                        var results = _journalSearchService.FindFile(query);
                        this.BeginInvoke(new Action(() => PostMessage("whereDidItGoResults", results)));
                    });
                }
                else if (raw.StartsWith("set:contextMenu:"))
                {
                    bool enable = raw["set:contextMenu:".Length..] == "true";
                    if (enable)
                        WindowsShellIntegration.RegisterContextMenu(Application.ExecutablePath);
                    else
                        WindowsShellIntegration.UnregisterContextMenu();
                    Log(enable ? "Menu contestuale Esplora Risorse registrato." : "Menu contestuale rimosso.");
                }
                else if (raw.StartsWith("set:deleteEmpty:"))
                {
                    _coreSettings.DeleteEmptyFolders = raw["set:deleteEmpty:".Length..] == "true";
                    _activeProfile.DeleteEmptyFolders = _coreSettings.DeleteEmptyFolders;
                    SaveCoreSettings();
                    SaveProfiles();
                }
                else if (raw.StartsWith("set:deleteDuplicates:"))
                {
                    _coreSettings.DeleteDuplicates = raw["set:deleteDuplicates:".Length..] == "true";
                    _activeProfile.DeleteDuplicates = _coreSettings.DeleteDuplicates;
                    SaveCoreSettings();
                    SaveProfiles();
                }
                else if (raw.StartsWith("set:compress:"))
                {
                    _coreSettings.CompressOldFiles = raw["set:compress:".Length..] == "true";
                    _activeProfile.CompressOldFiles = _coreSettings.CompressOldFiles;
                    SaveCoreSettings();
                    SaveProfiles();
                }
                else if (raw.StartsWith("set:includeSub:"))
                {
                    _coreSettings.IncludeSubfolders = raw["set:includeSub:".Length..] == "true";
                    _activeProfile.IncludeSubfolders = _coreSettings.IncludeSubfolders;
                    SaveCoreSettings();
                    SaveProfiles();
                    UpdateWatchers();
                }
                else if (raw.StartsWith("set:startup:"))
                {
                    SetStartupRegistry(raw["set:startup:".Length..] == "true");
                }
                else if (raw == "browse")
                {
                    BrowseForSourceFolder();
                }
                else if (raw == "setDest")
                {
                    BrowseForDestinationFolder();
                }
                else if (raw == "showMoves")
                {
                    var json = JsonSerializer.Serialize(_sessionMoved);
                    _webView?.ExecuteScriptAsync($"showDetails('File Spostati', {JsonSerializer.Serialize(json)})");
                }
                else if (raw == "showDeleted")
                {
                    var json = JsonSerializer.Serialize(_sessionDeleted);
                    _webView?.ExecuteScriptAsync($"showDetails('File Cestinati', {JsonSerializer.Serialize(json)})");
                }
                else if (raw == "restartUpdate")
                {
                    _updateService?.RestartAndApply();
                }
                else if (raw == "close")
                {
                    this.Close();
                }
                else if (raw == "minimize")
                {
                    this.WindowState = FormWindowState.Minimized;
                }
                else if (raw == "help")
                {
                    OpenHelp();
                }
                else if (raw == "refresh")
                {
                    UpdateUIStats();
                    UpdateSourceUI();
                    Log("Interfaccia e statistiche aggiornate.");
                }
            }
            catch (Exception ex)
            {
                Log($"⚠️ Errore messaggio: {ex.Message}");
            }
        }

        // ---------------------------------------------------------------------------
        // Initial Data & UI Sync
        // ---------------------------------------------------------------------------

        private async void LoadInitialData()
        {
            if (_webView?.CoreWebView2 == null) return;
            await Task.Delay(300);

            try
            {
                // Send Profiles list
                var profilePayload = new
                {
                    profiles = _profiles.Select(p => new { Name = p.Name, Enabled = p.Enabled }),
                    active = _activeProfile.Name
                };
                await PostMessage("profiles", profilePayload);

                // Send Rules for active profile
                await PostMessage("rules", _activeProfile.Rules);

                // Settings Checkboxes
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
                bool startup = key?.GetValue("CosmOrganizer") != null;
                bool isCtx = WindowsShellIntegration.IsContextMenuRegistered();

                var jsSettings = $@"
                    setTimeout(() => {{
                        const chkDel = document.getElementById('chkDeleteEmpty');
                        const chkDup = document.getElementById('chkDuplicates');
                        const chkCmp = document.getElementById('chkCompress');
                        const chkSub = document.getElementById('chkSubfolders');
                        const chkSt  = document.getElementById('chkStartup');
                        const chkCtx = document.getElementById('chkContextMenu');
                        const selMod = document.getElementById('modeSelect');

                        if (chkDel) chkDel.checked = {_activeProfile.DeleteEmptyFolders.ToString().ToLower()};
                        if (chkDup) chkDup.checked = {_activeProfile.DeleteDuplicates.ToString().ToLower()};
                        if (chkCmp) chkCmp.checked = {_activeProfile.CompressOldFiles.ToString().ToLower()};
                        if (chkSub) chkSub.checked = {_activeProfile.IncludeSubfolders.ToString().ToLower()};
                        if (chkSt)  chkSt.checked  = {startup.ToString().ToLower()};
                        if (chkCtx) chkCtx.checked = {isCtx.ToString().ToLower()};
                        if (selMod) selMod.value   = '{_activeProfile.OrganizeMode}';
                    }}, 100);
                ";
                await _webView.ExecuteScriptAsync(jsSettings);

                UpdateSourceUI();
                UpdateUIStats();

                if (_showCorruptedWarning)
                {
                    _showCorruptedWarning = false;
                    await PostMessage("toast", "⚠️ Impostazioni danneggiate: ripristinati i valori predefiniti.");
                }

                await PostMessage("log", "Sistema pronto ✓");
            }
            catch { }
        }

        private void SwitchActiveProfile(string profileName)
        {
            var p = _profiles.FirstOrDefault(x => x.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));
            if (p != null)
            {
                _activeProfile = p;
                UpdateSourceUI();
                UpdateUIStats();
                UpdateWatchers();
                PostMessage("rules", _activeProfile.Rules);
                Log($"Profilo attivo: {_activeProfile.Name}");
            }
        }

        private void UpdateSourceUI()
        {
            if (_webView?.CoreWebView2 == null) return;
            var list = string.Join(", ", _activeProfile.SourceFolders.Select(p => Path.GetFileName(p)));
            if (string.IsNullOrEmpty(list)) list = "Trascina qui le cartelle da organizzare";
            var escaped = JsonSerializer.Serialize(list);
            _webView.ExecuteScriptAsync($"if(document.querySelector('#dropZone h3')) document.querySelector('#dropZone h3').innerText = {escaped};");
        }

        private void UpdateUIStats()
        {
            if (_webView?.CoreWebView2 == null) return;

            string dest = string.IsNullOrEmpty(_activeProfile.DestinationFolder)
                ? "Sottocartelle sorgente (default)"
                : _activeProfile.DestinationFolder;

            var statsObj = new
            {
                files = _sessionMoved.Count,
                space = "0 MB",
                isMonitoring = _isRealTimeEnabled && (!_pauseUntil.HasValue || DateTime.Now >= _pauseUntil.Value),
                deletedCount = _sessionDeleted.Count
            };
            PostMessage("stats", statsObj);

            var escapedDest = JsonSerializer.Serialize(dest);
            _webView.ExecuteScriptAsync($"if(document.getElementById('destPath')) document.getElementById('destPath').innerText = {escapedDest};");
        }

        // ---------------------------------------------------------------------------
        // Preview
        // ---------------------------------------------------------------------------

        private void RequestPreview()
        {
            Task.Run(() =>
            {
                try
                {
                    var items = _orgService.Preview(_activeProfile);
                    this.BeginInvoke(new Action(() =>
                    {
                        PostMessage("previewData", items);
                        Log($"Anteprima generata: {items.Count} file analizzati.");
                    }));
                }
                catch (Exception ex)
                {
                    this.BeginInvoke(new Action(() => Log($"⚠️ Errore anteprima: {ex.Message}")));
                }
            });
        }

        private void ApplyPreviewSelected(string jsonSelected)
        {
            try
            {
                var paths = JsonSerializer.Deserialize<List<string>>(jsonSelected) ?? new();
                if (paths.Count == 0)
                {
                    Log("Nessun file selezionato dall'anteprima.");
                    return;
                }

                // Run only selected paths
                Log($"🚀 Esecuzione su {paths.Count} file selezionati...");
                _currentRunId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_") + Guid.NewGuid().ToString("N")[..8];
                _runCts = new CancellationTokenSource();

                Task.Run(() =>
                {
                    int ok = 0;
                    foreach (var path in paths)
                    {
                        if (!File.Exists(path)) continue;
                        try
                        {
                            var dest = Path.Combine(
                                string.IsNullOrEmpty(_activeProfile.DestinationFolder)
                                    ? Path.GetDirectoryName(path)!
                                    : _activeProfile.DestinationFolder,
                                _categoryService.GetCategory(path),
                                Path.GetFileName(path));

                            _safeFile.MoveFile(path, dest);
                            _journal.AppendEntry(new FileOperation(_currentRunId, DateTime.UtcNow, "moved", path, dest));
                            _sessionMoved.Add(new { Name = Path.GetFileName(path), Old = path, New = dest });
                            ok++;
                        }
                        catch { }
                    }

                    this.BeginInvoke(new Action(() =>
                    {
                        Log($"✅ Spostati {ok} file con successo.");
                        UpdateUIStats();
                        try { SystemSounds.Asterisk.Play(); } catch { }
                    }));
                });
            }
            catch (Exception ex)
            {
                Log($"⚠️ Errore applicazione anteprima: {ex.Message}");
            }
        }

        // ---------------------------------------------------------------------------
        // Organize & Undo
        // ---------------------------------------------------------------------------

        private void OrganizeActiveProfile()
        {
            RunProfile(_activeProfile);
        }

        private void RunProfile(Profile profile)
        {
            if (profile.SourceFolders.Count == 0)
            {
                Log($"⚠️ Nessuna cartella sorgente impostata nel profilo '{profile.Name}'.");
                return;
            }

            Log($"🚀 Avvio organizzazione profilo '{profile.Name}'...");
            _currentRunId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_") + Guid.NewGuid().ToString("N")[..8];
            _runCts = new CancellationTokenSource();

            var progress = new Progress<(int Percent, string File)>(p =>
            {
                PostMessage("progress", new { percent = p.Percent, label = $"Elaborazione: {Path.GetFileName(p.File)}" });
            });

            Task.Run(() =>
            {
                var (moved, errors) = _orgService.Run(profile, _currentRunId, _runCts.Token, progress);
                this.BeginInvoke(new Action(() =>
                {
                    PostMessage("progress", new { percent = 100, label = "Completato" });
                    Log($"✅ Organizzazione completata: {moved} file ordinati, {errors} errori.");
                    UpdateUIStats();
                    try { SystemSounds.Asterisk.Play(); } catch { }
                }));
            });
        }

        private async void UndoOperations()
        {
            if (string.IsNullOrEmpty(_currentRunId))
            {
                Log("⚠️ Nessuna operazione recente da annullare.");
                return;
            }

            Log("🔄 Annullamento operazioni in corso...");
            await Task.Run(() =>
            {
                var results = _journal.UndoRun(_currentRunId, _safeFile);
                foreach (var (op, success, msg) in results)
                {
                    var status = success ? "✓" : "✗";
                    Log($"  {status} {Path.GetFileName(op.Source)}: {msg}");
                }
            });

            Log("✅ Annullamento completato.");
            _currentRunId = string.Empty;
            _sessionMoved.Clear();
            _sessionDeleted.Clear();
            UpdateUIStats();
        }

        // ---------------------------------------------------------------------------
        // Real-Time Watcher
        // ---------------------------------------------------------------------------

        private void ToggleRealTimeMonitoring()
        {
            _isRealTimeEnabled = !_isRealTimeEnabled;
            _activeProfile.RealTimeEnabled = _isRealTimeEnabled;
            SaveProfiles();
            UpdateWatchers();
            Log(_isRealTimeEnabled ? "Monitoraggio Real-time Attivo ⚡" : "Monitoraggio Disattivato");
            UpdateUIStats();
        }

        private void UpdateWatchers()
        {
            _realTime?.Dispose();
            _realTime = null;

            if (_isRealTimeEnabled && _activeProfile.SourceFolders.Count > 0)
            {
                _realTime = new RealTimeService(_safeFile);
                _realTime.FileReady += path =>
                {
                    if (_pauseUntil.HasValue && DateTime.Now < _pauseUntil.Value) return;
                    this.BeginInvoke(new Action(() =>
                    {
                        Log($"⚡ Rilevato nuovo file: {Path.GetFileName(path)}");
                        RunProfile(_activeProfile);
                    }));
                };
                _realTime.ScanRequired += msg => Log($"⚠️ Riscansione richiesta: {msg}");

                foreach (var src in _activeProfile.SourceFolders)
                {
                    if (Directory.Exists(src))
                        _realTime.Watch(src, _activeProfile.IncludeSubfolders);
                }
            }
        }

        // ---------------------------------------------------------------------------
        // Rules Testing & Creation
        // ---------------------------------------------------------------------------

        private void TestRuleOnFile(string ruleId)
        {
            var rule = _activeProfile.Rules.FirstOrDefault(r => r.Id == ruleId);
            if (rule == null) return;

            using var ofd = new OpenFileDialog { Title = $"Seleziona un file per provare la regola '{rule.Name}'" };
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                var match = _ruleEngine.Evaluate(ofd.FileName, new List<CustomRule> { rule });
                if (match != null)
                {
                    MessageBox.Show(
                        $"Regola CORRISPONDE!\n\nFile: {Path.GetFileName(ofd.FileName)}\nDestinazione prevista: {match.ResolvedDestination}\nAzione: {rule.Action}",
                        "Test Regola - Successo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show(
                        $"La regola NON corrisponde a questo file.\n\nFile: {Path.GetFileName(ofd.FileName)}",
                        "Test Regola - Nessuna Corrispondenza", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void AddNewRuleDialog()
        {
            using var dlg = new Form
            {
                Text = "Nuova Regola Personalizzata",
                Size = new Size(420, 320),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false
            };

            var lblName = new Label { Text = "Nome regola:", Location = new Point(20, 20), AutoSize = true };
            var txtName = new TextBox { Location = new Point(20, 45), Size = new Size(360, 25), Text = "Fatture PDF" };

            var lblExt = new Label { Text = "Estensioni (es. .pdf, .docx):", Location = new Point(20, 85), AutoSize = true };
            var txtExt = new TextBox { Location = new Point(20, 110), Size = new Size(360, 25), Text = ".pdf" };

            var lblDest = new Label { Text = "Cartella destinazione:", Location = new Point(20, 150), AutoSize = true };
            var txtDest = new TextBox { Location = new Point(20, 175), Size = new Size(360, 25), Text = "Documenti\\Fatture" };

            var btnOk = new Button { Text = "Salva", Location = new Point(140, 225), Size = new Size(120, 36) };
            btnOk.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txtName.Text)) return;
                var exts = txtExt.Text.Split(',').Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                var newRule = new CustomRule
                {
                    Name = txtName.Text.Trim(),
                    Order = _activeProfile.Rules.Count,
                    Conditions = new() { new RuleCondition { Extensions = exts } },
                    Action = RuleAction.Move,
                    DestinationTemplate = txtDest.Text.Trim()
                };
                _activeProfile.Rules.Add(newRule);
                SaveProfiles();
                PostMessage("rules", _activeProfile.Rules);
                dlg.Close();
            };

            dlg.Controls.AddRange(new Control[] { lblName, txtName, lblExt, txtExt, lblDest, txtDest, btnOk });
            dlg.ShowDialog(this);
        }

        // ---------------------------------------------------------------------------
        // Helpers & Bridge
        // ---------------------------------------------------------------------------

        private void BrowseForSourceFolder()
        {
            using var d = new FolderBrowserDialog { Description = "Seleziona cartella da organizzare" };
            if (d.ShowDialog() == DialogResult.OK)
            {
                if (!_activeProfile.SourceFolders.Contains(d.SelectedPath))
                {
                    _activeProfile.SourceFolders.Add(d.SelectedPath);
                    SaveProfiles();
                    UpdateSourceUI();
                    UpdateWatchers();
                    Log($"Cartella aggiunta al profilo '{_activeProfile.Name}': {Path.GetFileName(d.SelectedPath)}");
                }
            }
        }

        private void BrowseForDestinationFolder()
        {
            using var d = new FolderBrowserDialog { Description = "Seleziona cartella di destinazione" };
            if (d.ShowDialog() == DialogResult.OK)
            {
                _activeProfile.DestinationFolder = d.SelectedPath;
                SaveProfiles();
                UpdateUIStats();
                Log($"Destinazione impostata a: {d.SelectedPath}");
            }
        }

        private void SetStartupRegistry(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                if (enable)
                    key?.SetValue("CosmOrganizer", Application.ExecutablePath);
                else
                    key?.DeleteValue("CosmOrganizer", false);
            }
            catch { }
        }

        private Task PostMessage(string type, object payload)
        {
            if (_webView?.CoreWebView2 == null) return Task.CompletedTask;
            var json = JsonSerializer.Serialize(new { type, payload });
            return _webView.CoreWebView2.ExecuteScriptAsync($"handleMessage({json})");
        }

        private void Log(string msg)
        {
            try
            {
                if (_webView?.CoreWebView2 != null && !_webView.IsDisposed)
                {
                    BeginInvoke(new Action(() => PostMessage("log", msg)));
                }
            }
            catch { }
        }

        private void SaveWindowPosition()
        {
            try
            {
                if (this.WindowState == FormWindowState.Normal)
                {
                    _coreSettings.WindowLeft = this.Location.X;
                    _coreSettings.WindowTop = this.Location.Y;
                    _coreSettings.WindowWidth = this.Size.Width;
                    _coreSettings.WindowHeight = this.Size.Height;
                    _coreSettings.IsMaximized = false;
                }
                else if (this.WindowState == FormWindowState.Maximized)
                {
                    _coreSettings.IsMaximized = true;
                    _coreSettings.WindowLeft = this.RestoreBounds.Location.X;
                    _coreSettings.WindowTop = this.RestoreBounds.Location.Y;
                    _coreSettings.WindowWidth = this.RestoreBounds.Size.Width;
                    _coreSettings.WindowHeight = this.RestoreBounds.Size.Height;
                }
                SaveCoreSettings();
            }
            catch { }
        }

        private void OnFormClosing(object? sender, FormClosingEventArgs e)
        {
            SaveWindowPosition();
            // If user closed with X and tray is active, minimize to tray
            if (e.CloseReason == CloseReason.UserClosing && _trayIcon != null)
            {
                e.Cancel = true;
                this.Hide();
                _trayIcon.ShowBalloonTip(2000, "CosmOrganizer", "CosmOrganizer continua a proteggere i tuoi file in background.", ToolTipIcon.Info);
                return;
            }

            _realTime?.Dispose();
            _scheduleTimer?.Dispose();
            _trayIcon?.Dispose();
            _updateService?.ApplyOnShutdown();
        }

        // ---------------------------------------------------------------------------
        // Application Entry Point
        // ---------------------------------------------------------------------------

        [STAThread]
        static void Main(string[] args)
        {
            VelopackApp.Build().Run();

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new CosmOrganizerForm(args));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Errore imprevisto:\n{ex.Message}\n\n{ex.StackTrace}", "Errore CosmOrganizer", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
