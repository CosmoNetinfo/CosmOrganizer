using System;
using System.IO;
using System.Windows.Forms;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Media;
using System.Diagnostics;
using Microsoft.Win32;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Velopack;
using CosmOrganizer.Core;
using CosmOrganizer.Core;

namespace CosmOrganizer
{
    public class CosmOrganizerForm : Form
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private WebView2 webView;
        private ListBox lstSourceFolders;
        private Button btnAddFolder, btnRemoveFolder, btnOrganize, btnUndo, btnCancel, btnClearList;
        private TextBox txtDestination, txtLog;
        private Button btnBrowseDestination, btnFilters, btnSchedule, btnPreview;
        private ComboBox cmbOrganizeBy, cmbLanguage;
        private CheckBox chkIncludeSubfolders, chkCopyInsteadOfMove, chkDarkMode;
        private ProgressBar progressBarScan, progressBarOrganize;
        private Label lblScanStatus, lblOrganizeStatus, lblFileCount, lblAppTitle, lblVersion;
        private System.Windows.Forms.Timer scheduleTimer;
        private PictureBox picLogo;
        private Panel headerPanel;

        private List<string> excludedExtensions = new List<string>();
        private List<string> excludedFolders = new List<string>();
        private long minFileSize = 0, maxFileSize = long.MaxValue;
        private bool isScheduleEnabled = false, scheduleDaily = false, isDarkMode = true;
        private DateTime scheduledTime;
        private List<object> sessionMoved = new List<object>();
        private List<string> sessionDeleted = new List<string>();
        private System.Threading.CancellationTokenSource cancellationTokenSource;
        private Dictionary<string, Dictionary<string, string>> translations;
        private string currentLanguage = "it";
        private UpdateService updateService;

        // Real-time Monitoring
        private CheckBox chkRealTime;
        private bool isRealTimeEnabled = false;

        // --- Core services ---
        private readonly SafeFileService _safeFile = new();
        private readonly SettingsService _settingsService = new();
        private readonly JournalService _journal = new();
        private RealTimeService? _realTime;
        private string _currentRunId = string.Empty;
        private readonly HashSet<string> _dirsEmptiedThisRun = new();
        // Core settings (replaces the old internal AppSettings)
        private CosmOrganizer.Core.AppSettings _coreSettings = new();
        private bool _showCorruptedWarning;

        public CosmOrganizerForm()
        {
            // Imposta dimensioni finestra
            this.Size = new System.Drawing.Size(1200, 850);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MinimumSize = new System.Drawing.Size(1000, 750);

            InitializeTranslations();
            LoadSettings();

            // Modern UI Initialization
            InitializeWebView();

            // Auto Update Service via Velopack
            updateService = new UpdateService(() => _coreSettings.AutoUpdateEnabled, OnUpdateReady);
            updateService.Start();

            this.FormClosing += (s, e) =>
            {
                _realTime?.Dispose();
                updateService?.ApplyOnShutdown();
            };
        }

        private void OnUpdateReady(string newVersion)
        {
            if (this.IsDisposed || !this.IsHandleCreated) return;
            this.BeginInvoke(new Action(() =>
            {
                webView?.CoreWebView2?.ExecuteScriptAsync($"showUpdateBanner('{newVersion}');");
            }));
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
                int darkMode = 1;
                DwmSetWindowAttribute(this.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
            }
            catch { }
        }

        private void CosmOrganizerForm_KeyDown(object sender, KeyEventArgs e)
        {
            // Legacy keyboard shortcuts disabled for WebView UI
            // Can be re-enabled with appropriate WebView message passing
        }

        public void AddFolderFromArgs(string folderPath)
        {
            this.Load += (s, e) =>
            {
                if (!_coreSettings.SourceFolders.Contains(folderPath))
                {
                    _coreSettings.SourceFolders.Add(folderPath);
                    SaveSettings();
                    RefreshFileCount();
                    UpdateWatchers();
                    UpdateSourceUI();
                }
            };
        }

        private void PlayCompletionSound()
        {
            try { SystemSounds.Asterisk.Play(); }
            catch { }
        }

        private void InitializeTranslations()
        {
            translations = new Dictionary<string, Dictionary<string, string>>
            {
                ["it"] = new Dictionary<string, string>
                {
                    {"title", "CosmOrganizer"}, {"subtitle", "Organizzatore Automatico di File"},
                    {"sourceFolders", "Cartelle da analizzare:"}, {"addFolder", "Aggiungi"},
                    {"removeFolder", "Rimuovi"}, {"clearList", "Svuota"},
                    {"includeSubfolders", "Includi sottocartelle"}, {"copyMode", "Copia (non spostare)"},
                    {"totalFiles", "File totali:"}, {"scanStatus", "In attesa"},
                    {"destination", "Cartella destinazione:"}, {"browse", "Sfoglia..."},
                    {"organizeBy", "Organizza per:"}, {"organize", "ORGANIZZA"},
                    {"cancel", "ANNULLA"}, {"restore", "RIPRISTINA"}, {"preview", "ANTEPRIMA"},
                    {"status", "In attesa"}, {"log", "Log:"}, {"copyLog", "Copia"},
                    {"darkMode", "Tema Scuro"}, {"filters", "Filtri"}, {"schedule", "Pianifica"},
                    {"byType", "Tipo file"}, {"byDate", "Data creazione"}, {"byMod", "Data modifica"},
                    {"byExt", "Estensione"}, {"byLetter", "Prima lettera"}, {"bySize", "Dimensione"},
                    {"scanning", "Scansione..."}, {"complete", "Completato"}, {"files", "file"},
                    {"processing", "Elaborazione..."}, {"errors", "errori"},
                    {"filterTitle", "Filtri"}, {"excludeExt", "Escludi estensioni:"},
                    {"excludeDir", "Escludi cartelle:"}, {"minSize", "Min MB:"}, {"maxSize", "Max MB:"},
                    {"schedTitle", "Pianificazione"}, {"schedEnable", "Abilita"},
                    {"schedTime", "Orario:"}, {"schedDaily", "Ogni giorno"}, {"save", "Salva"},
                    {"realTime", "Monitoraggio Real-time ⚡"},
                    {"rules", "Regole..."}, {"rulesTitle", "Regole Personalizzate"},
                    {"keyword", "Parola Chiave"}, {"matchType", "Condizione"}, {"targetFolder", "Sottocartella"},
                    {"addRule", "Aggiungi Regola"}, {"deleteRule", "Elimina"},
                    {"deleteEmpty", "Elimina cartelle vuote"},
                    {"contains", "Contiene"}, {"starts", "Inizia con"}, {"ends", "Finisce con"},
                    {"compressTitle", "Compressione"}, {"compressEnable", "Comprimi file vecchi (> giorni):"},
                    {"stats", "Statistiche"}, {"statsTitle", "Statistiche Utilizzo"},
                    {"totalOrg", "File Organizzati:"}, {"totalComp", "File Compressi:"}, {"since", "Dal:"}
                },
                ["en"] = new Dictionary<string, string>
                {
                    {"title", "CosmOrganizer"}, {"subtitle", "Automatic File Organizer"},
                    {"sourceFolders", "Folders to analyze:"}, {"addFolder", "Add"},
                    {"removeFolder", "Remove"}, {"clearList", "Clear"},
                    {"includeSubfolders", "Include subfolders"}, {"copyMode", "Copy (don't move)"},
                    {"totalFiles", "Total files:"}, {"scanStatus", "Waiting"},
                    {"destination", "Destination folder:"}, {"browse", "Browse..."},
                    {"organizeBy", "Organize by:"}, {"organize", "ORGANIZE"},
                    {"cancel", "CANCEL"}, {"restore", "RESTORE"}, {"preview", "PREVIEW"},
                    {"status", "Waiting"}, {"log", "Log:"}, {"copyLog", "Copy"},
                    {"darkMode", "Dark Mode"}, {"filters", "Filters"}, {"schedule", "Schedule"},
                    {"byType", "File type"}, {"byDate", "Creation date"}, {"byMod", "Modified date"},
                    {"byExt", "Extension"}, {"byLetter", "First letter"}, {"bySize", "Size"},
                    {"scanning", "Scanning..."}, {"complete", "Complete"}, {"files", "files"},
                    {"processing", "Processing..."}, {"errors", "errors"},
                    {"filterTitle", "Filters"}, {"excludeExt", "Exclude extensions:"},
                    {"excludeDir", "Exclude folders:"}, {"minSize", "Min MB:"}, {"maxSize", "Max MB:"},
                    {"schedTitle", "Scheduling"}, {"schedEnable", "Enable"},
                    {"schedTime", "Time:"}, {"schedDaily", "Daily"}, {"save", "Save"},
                    {"realTime", "Real-time Monitoring ⚡"},
                    {"rules", "Rules..."}, {"rulesTitle", "Custom Rules"},
                    {"keyword", "Keyword"}, {"matchType", "Condition"}, {"targetFolder", "Subfolder"},
                    {"addRule", "Add Rule"}, {"deleteRule", "Delete"},
                    {"deleteEmpty", "Delete empty folders"},
                    {"contains", "Contains"}, {"starts", "Starts with"}, {"ends", "Ends with"},
                    {"compressTitle", "Compression"}, {"compressEnable", "Compress old files (> days):"},
                    {"stats", "Statistics"}, {"statsTitle", "Usage Statistics"},
                    {"totalOrg", "Files Organized:"}, {"totalComp", "Files Compressed:"}, {"since", "Since:"}
                }
            };
        }

        private string T(string k) => translations.ContainsKey(currentLanguage) && translations[currentLanguage].ContainsKey(k) ? translations[currentLanguage][k] : k;

        private async void InitializeWebView()
        {
            // Modern Window Setup
            Text = "CosmOrganizer";
            FormBorderStyle = FormBorderStyle.Sizable;
            BackColor = Color.FromArgb(5, 7, 10); // Match CSS --bg-dark

            // WebView Setup
            webView = new WebView2();
            webView.Dock = DockStyle.Fill;
            webView.AllowExternalDrop = true; // Allow dropping files from Explorer
            Controls.Add(webView);

            try
            {
                var userFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CosmOrganizer", "WebView2");
                if (!Directory.Exists(userFolder)) Directory.CreateDirectory(userFolder);
                var env = await CoreWebView2Environment.CreateAsync(null, userFolder);
                await webView.EnsureCoreWebView2Async(env);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Errore inizializzazione UI: {ex.Message}", "Errore", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Interface", "index.html");

            // Debug: Show the path and check if file exists
            System.Diagnostics.Debug.WriteLine($"Loading HTML from: {htmlPath}");
            System.Diagnostics.Debug.WriteLine($"File exists: {File.Exists(htmlPath)}");
            System.Diagnostics.Debug.WriteLine($"Base directory: {AppDomain.CurrentDomain.BaseDirectory}");

            if (!File.Exists(htmlPath))
            {
                MessageBox.Show($"File HTML non trovato!\n\nPercorso cercato:\n{htmlPath}\n\nBase directory:\n{AppDomain.CurrentDomain.BaseDirectory}",
                    "Errore", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Load HTML with proper file:// URI
            var uri = new Uri(htmlPath);
            webView.Source = uri;

            // Event Handlers
            webView.WebMessageReceived += WebView_WebMessageReceived;
            webView.NavigationCompleted += (s, e) =>
            {
                UpdateUIStats();
                var ver = typeof(CosmOrganizerForm).Assembly.GetName().Version;
                string verStr = ver != null ? $"{ver.Major}.{ver.Minor}" : "3.3";
                webView.ExecuteScriptAsync($"setAppVersion('{verStr}');");
            };

            // Native Drag & Drop Implementation
            webView.DragEnter += (s, e) =>
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
            };
            webView.DragDrop += (s, e) =>
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    foreach (string path in files)
                    {
                        if (Directory.Exists(path) && !_coreSettings.SourceFolders.Contains(path))
                        {
                            _coreSettings.SourceFolders.Add(path);
                            SaveSettings();
                            // Notify UI of success (Visual feedback)
                            if (webView != null && webView.CoreWebView2 != null)
                            {
                                webView.ExecuteScriptAsync($"addLog('Cartella aggiunta: {Path.GetFileName(path).Replace("\\", "\\\\")}');");
                            }
                        }
                    }
                    RefreshFileCount();
                    UpdateSourceUI();
                    SystemSounds.Asterisk.Play();
                }
            };
        }

        private void UpdateSourceUI()
        {
            if (webView == null || webView.CoreWebView2 == null) return;
            var list = string.Join(", ", _coreSettings.SourceFolders.Select(p => Path.GetFileName(p)));
            if (string.IsNullOrEmpty(list)) list = "Trascina qui le cartelle";
            webView.ExecuteScriptAsync($"if(document.querySelector('#dropZone h3')) document.querySelector('#dropZone h3').innerText = '{list.Replace("'", "\\'")}';");
        }

        private void UpdateUIStats()
        {
            if (webView == null || webView.IsDisposed) return;

            Invoke(new Action(async () => {
                if (webView.CoreWebView2 == null) return;

                bool isMon = isRealTimeEnabled;
                string dest = string.IsNullOrEmpty(_coreSettings.LastDestinationFolder) ? "Non selezionata" : _coreSettings.LastDestinationFolder;

                var js = $@"
                    if(typeof updateStats === 'function') updateStats(0, '0 MB', {isMon.ToString().ToLower()}, 0);
                    if(document.getElementById('destPath')) document.getElementById('destPath').innerText = '{dest.Replace("\\", "\\\\")}';
                ";
                await webView.ExecuteScriptAsync(js);
            }));
        }

        private async void LoadInitialData()
        {
            if (webView == null || webView.CoreWebView2 == null) return;

            // Wait a bit for DOM to be fully ready
            await Task.Delay(500);

            try
            {
                // Load Settings checkboxes - use setTimeout to ensure elements exist
                var jsCode = $@"
                    setTimeout(() => {{
                        const chkDelete = document.getElementById('chkDeleteEmpty');
                        const chkCompress = document.getElementById('chkCompress');
                        const chkSub = document.getElementById('chkSubfolders');
                        if (chkDelete) chkDelete.checked = {_coreSettings.DeleteEmptyFolders.ToString().ToLower()};
                        if (chkCompress) chkCompress.checked = {_coreSettings.CompressOldFiles.ToString().ToLower()};
                        if (chkSub) chkSub.checked = {_coreSettings.IncludeSubfolders.ToString().ToLower()};
                    }}, 100);
                ";
                await webView.ExecuteScriptAsync(jsCode);

                // Read registry to show real startup toggle state
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
                bool startupEnabled = key?.GetValue("CosmOrganizer") != null;
                await webView.ExecuteScriptAsync($"setTimeout(() => {{ const chkStartup = document.getElementById('chkStartup'); if(chkStartup) chkStartup.checked = {startupEnabled.ToString().ToLower()}; }}, 150);");

                if (_showCorruptedWarning)
                {
                    _showCorruptedWarning = false;
                    await PostMessage("toast", "⚠️ Impostazioni danneggiate: ripristinati i valori predefiniti.");
                }

                // Initial log
                await webView.ExecuteScriptAsync("addLog('Sistema inizializzato ✓');");
                UpdateSourceUI();
            }
            catch (Exception ex)
            {
                // Log error for debugging
                System.Diagnostics.Debug.WriteLine($"LoadInitialData error: {ex.Message}");
            }
        }

        private async void WebView_WebMessageReceived(object sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
        {
            try {
                var msg = e.TryGetWebMessageAsString();

                if (msg == "pageReady")
                {
                    // JavaScript is ready, now load initial data
                    LoadInitialData();
                }
                else if (msg == "toggleRealTime")
                {
                    Invoke(new Action(() => {
                        ToggleRealTimeMonitoring(); // Uses existing logic
                        UpdateUIStats();
                    }));
                }
                else if (msg.StartsWith("set:deleteEmpty:"))
                {
                    var val = msg.Substring("set:deleteEmpty:".Length);
                    _coreSettings.DeleteEmptyFolders = val == "true";
                    SaveSettings();
                }
                else if (msg.StartsWith("set:compress:"))
                {
                    var val = msg.Substring("set:compress:".Length);
                    _coreSettings.CompressOldFiles = val == "true";
                    SaveSettings();
                }
                else if (msg.StartsWith("set:startup:"))
                {
                    var val = msg.Substring("set:startup:".Length);
                    SetStartupRegistry(val == "true");
                }
                else if (msg == "restartUpdate")
                {
                    updateService?.RestartAndApply();
                }
                else if (msg.StartsWith("set:includeSub:"))
                {
                    var val = msg.Substring("set:includeSub:".Length);
                    _coreSettings.IncludeSubfolders = val == "true";
                    SaveSettings();
                    UpdateWatchers(); // Refresh watchers to apply subfolder setting
                }
                else if (msg == "organize")
                {
                    Invoke(new Action(() => OrganizeFiles()));
                }
                else if (msg == "undo")
                {
                    Invoke(new Action(() => UndoOperations()));
                }
                else if (msg == "showMoves")
                {
                    var json = JsonSerializer.Serialize(sessionMoved);
                    webView.ExecuteScriptAsync($"showDetails('File Spostati', '{json.Replace("'", "\\'").Replace("\\", "\\\\")}')");
                }
                else if (msg == "showDeleted")
                {
                    var json = JsonSerializer.Serialize(sessionDeleted);
                    webView.ExecuteScriptAsync($"showDetails('File Duplicati Eliminati', '{json.Replace("'", "\\'").Replace("\\", "\\\\")}')");
                }
                else if (msg == "browse")
                {
                   Invoke(new Action(() => {
                       using (var d = new FolderBrowserDialog())
                       {
                           if (d.ShowDialog() == DialogResult.OK)
                           {
                               if (!_coreSettings.SourceFolders.Contains(d.SelectedPath))
                               {
                                   _coreSettings.SourceFolders.Add(d.SelectedPath);
                                   SaveSettings();
                                   RefreshFileCount();
                                   UpdateSourceUI();
                                   Log($"Selezionata: {Path.GetFileName(d.SelectedPath)}");
                               }
                           }
                       }
                       UpdateUIStats();
                   }));
                }
                else if (msg == "setDest")
                {
                   Invoke(new Action(() => {
                       using (var d = new FolderBrowserDialog())
                       {
                           d.Description = "Seleziona dove salvare i file organizzati";
                           if (d.ShowDialog() == DialogResult.OK)
                           {
                               _coreSettings.LastDestinationFolder = d.SelectedPath;
                               SaveSettings();
                               UpdateUIStats();
                           }
                       }
                   }));
                }
                else if (msg == "close") Invoke(new Action(() => Close()));
                else if (msg == "minimize") Invoke(new Action(() => WindowState = FormWindowState.Minimized));
                else if (msg == "help")
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "https://www.cosmonet.info/scrivici/",
                            UseShellExecute = true
                        });
                    }
                    catch { }
                }

            } catch {}
        }

        private void SetStartupRegistry(bool enable)
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (enable)
                    {
                        key?.SetValue("CosmOrganizer", Application.ExecutablePath);
                    }
                    else
                    {
                        key?.DeleteValue("CosmOrganizer", false);
                    }
                }
            }
            catch { }
        }

        private void ToggleRealTimeMonitoring()
        {
            isRealTimeEnabled = !isRealTimeEnabled;
            SaveSettings();
            UpdateWatchers();
            Log(isRealTimeEnabled ? "Monitoraggio Attivato ⚡" : "Monitoraggio Disattivato");
        }

        private void InitializeUI()
        {
            // Legacy UI initialization is suppressed in favor of InitializeWebView
        }

        private void ApplyTheme()
        {
            if (isDarkMode)
            {
                BackColor = Color.FromArgb(26, 29, 41);
                if (headerPanel != null) headerPanel.BackColor = Color.FromArgb(36, 40, 55);
                ForeColor = Color.FromArgb(248, 250, 252);

                foreach (Control c in Controls)
                {
                    if (c is Label) c.ForeColor = Color.FromArgb(248, 250, 252);
                    if (c is ListBox) { c.BackColor = Color.FromArgb(26, 29, 41); c.ForeColor = Color.FromArgb(248, 250, 252); }
                    if (c is TextBox && c != txtLog) { c.BackColor = Color.FromArgb(26, 29, 41); c.ForeColor = Color.FromArgb(248, 250, 252); }
                    if (c is CheckBox) c.ForeColor = Color.FromArgb(248, 250, 252);
                    if (c is ComboBox) { c.BackColor = Color.FromArgb(26, 29, 41); c.ForeColor = Color.FromArgb(248, 250, 252); }
                }

                if (txtLog != null)
                {
                    txtLog.BackColor = Color.FromArgb(15, 15, 20);
                    txtLog.ForeColor = Color.FromArgb(16, 185, 129);
                }
            }
            else
            {
                BackColor = Color.FromArgb(248, 250, 252);
                if (headerPanel != null) headerPanel.BackColor = Color.White;
                ForeColor = Color.FromArgb(30, 41, 59);

                foreach (Control c in Controls)
                {
                    if (c is Label) c.ForeColor = Color.FromArgb(30, 41, 59);
                    if (c is ListBox) { c.BackColor = Color.White; c.ForeColor = Color.FromArgb(30, 41, 59); }
                    if (c is TextBox && c != txtLog) { c.BackColor = Color.White; c.ForeColor = Color.FromArgb(30, 41, 59); }
                    if (c is CheckBox) c.ForeColor = Color.FromArgb(30, 41, 59);
                    if (c is ComboBox) { c.BackColor = Color.White; c.ForeColor = Color.FromArgb(30, 41, 59); }
                }

                if (txtLog != null)
                {
                    txtLog.BackColor = Color.FromArgb(30, 41, 59);
                    txtLog.ForeColor = Color.FromArgb(16, 185, 129);
                }
            }
        }

        private async void RefreshFileCount()
        {
            if (webView == null || webView.IsDisposed) return;

            await Task.Run(() =>
            {
                var files = GetFiles();
                Invoke(new Action(async () => {
                    if (webView != null && webView.CoreWebView2 != null)
                    {
                        await webView.ExecuteScriptAsync($"addLog('Totali: {files.Count} file');");
                    }
                }));
            });
        }

        private List<string> GetFiles()
        {
            var f = new List<string>();
            foreach (string d in _coreSettings.SourceFolders)
            {
                if (!Directory.Exists(d))
                {
                    Log($"Cartella non trovata: {d}");
                    continue;
                }
                try
                {
                    var opts = new EnumerationOptions
                    {
                        IgnoreInaccessible = true,
                        RecurseSubdirectories = _coreSettings.IncludeSubfolders,
                        AttributesToSkip = FileAttributes.ReparsePoint
                    };
                    var found = Directory.GetFiles(d, "*.*", opts);
                    if (found.Length == 0) Log($"Nessun file trovato in: {d}");

                    foreach (var file in found)
                    {
                        var fi = new FileInfo(file);
                        if (excludedExtensions.Contains(fi.Extension.ToLower())) continue;
                        if (excludedFolders.Any(ef => file.Contains(ef))) continue;
                        if (fi.Length < minFileSize || fi.Length > maxFileSize) continue;
                        f.Add(file);
                    }
                }
                catch (Exception ex)
                {
                    Log($"Errore scansione {d}: {ex.Message}");
                }
            }
            return f;
        }

        private void BtnFilters_Click(object s, EventArgs e)
        {
            using (var dlg = new Form { Text = T("filterTitle"), Size = new Size(450, 350), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false })
            {
                var lblExt = new Label { Text = T("excludeExt"), Location = new Point(15, 15), AutoSize = true };
                var txtExt = new TextBox { Location = new Point(15, 40), Size = new Size(400, 25), Text = string.Join(", ", excludedExtensions) };
                var lblDir = new Label { Text = T("excludeDir"), Location = new Point(15, 80), AutoSize = true };
                var txtDir = new TextBox { Location = new Point(15, 105), Size = new Size(400, 25), Text = string.Join(", ", excludedFolders) };
                var lblMin = new Label { Text = T("minSize"), Location = new Point(15, 145), AutoSize = true };
                var numMin = new NumericUpDown { Location = new Point(15, 170), Size = new Size(180, 25), Maximum = 10000, Value = (decimal)(minFileSize / 1048576) };
                var lblMax = new Label { Text = T("maxSize"), Location = new Point(235, 145), AutoSize = true };
                var numMax = new NumericUpDown { Location = new Point(235, 170), Size = new Size(180, 25), Maximum = 100000, Value = maxFileSize == long.MaxValue ? 0 : (decimal)(maxFileSize / 1048576) };

                var btnSave = new Button { Text = T("save"), Location = new Point(150, 230), Size = new Size(130, 40), FlatStyle = FlatStyle.Flat };
                btnSave.Click += (ss, ee) =>
                {
                    excludedExtensions = txtExt.Text.Split(',').Select(x => x.Trim().ToLower()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                    excludedFolders = txtDir.Text.Split(',').Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                    minFileSize = (long)(numMin.Value * 1048576);
                    maxFileSize = numMax.Value == 0 ? long.MaxValue : (long)(numMax.Value * 1048576);
                    SaveSettings();
                    dlg.Close();
                };

                dlg.Controls.AddRange(new Control[] { lblExt, txtExt, lblDir, txtDir, lblMin, numMin, lblMax, numMax, btnSave });
                dlg.ShowDialog();
            }
        }

        private void BtnSchedule_Click(object s, EventArgs e)
        {
            using (var dlg = new Form { Text = T("schedTitle"), Size = new Size(400, 280), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false })
            {
                var chkEnable = new CheckBox { Text = T("schedEnable"), Location = new Point(15, 20), AutoSize = true, Checked = isScheduleEnabled };
                var lblTime = new Label { Text = T("schedTime"), Location = new Point(15, 60), AutoSize = true };
                var dtpTime = new DateTimePicker { Location = new Point(15, 85), Size = new Size(350, 25), Format = DateTimePickerFormat.Time, ShowUpDown = true, Value = scheduledTime == DateTime.MinValue ? DateTime.Now : scheduledTime };
                var chkDaily = new CheckBox { Text = T("schedDaily"), Location = new Point(15, 125), AutoSize = true, Checked = scheduleDaily };

                var btnSave = new Button { Text = T("save"), Location = new Point(120, 170), Size = new Size(130, 40), FlatStyle = FlatStyle.Flat };
                btnSave.Click += (ss, ee) =>
                {
                    isScheduleEnabled = chkEnable.Checked;
                    scheduledTime = dtpTime.Value;
                    scheduleDaily = chkDaily.Checked;
                    SaveSettings();
                    dlg.Close();
                };

                dlg.Controls.AddRange(new Control[] { chkEnable, lblTime, dtpTime, chkDaily, btnSave });
                dlg.ShowDialog();
            }
        }

        private void LoadSettings()
        {
            try
            {
                var (s, corrupted) = _settingsService.Load();
                _coreSettings = s;
                if (corrupted)
                {
                    // Show toast on next WebView ready
                    _showCorruptedWarning = true;
                }
                UpdateWatchers();
            }
            catch { _coreSettings = new(); }
        }

        private void SaveSettings()
        {
            try { _settingsService.Save(_coreSettings); }
            catch { }
        }

        private string GetCategory(string f)
        {
            var e = Path.GetExtension(f).ToLower();

            string[] imgs = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".svg", ".webp", ".ico", ".tiff", ".raw", ".heic", ".psd" };
            string[] vids = { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg", ".3gp" };
            string[] arch = { ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".iso", ".dmg" };
            string[] apps = { ".exe", ".msi", ".bat", ".cmd", ".sh", ".app", ".deb", ".rpm" };
            string[] audio = { ".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a" };
            string[] docs  = { ".pdf", ".doc", ".docx", ".odt", ".rtf", ".txt", ".md" };

            if (imgs.Contains(e)) return "Immagini";
            if (vids.Contains(e)) return "Video";
            if (arch.Contains(e)) return "Compressi";
            if (apps.Contains(e)) return "Applicazioni";
            if (audio.Contains(e)) return "Audio";
            if (docs.Contains(e)) return "Documenti";
            return "Miscellanea";
        }

        private bool AreFilesIdentical(string path1, string path2)
        {
            try
            {
                var fi1 = new FileInfo(path1);
                var fi2 = new FileInfo(path2);
                if (fi1.Length != fi2.Length) return false;

                const int bufferSize = 64 * 1024;
                using (var fs1 = new FileStream(path1, FileMode.Open, FileAccess.Read))
                using (var fs2 = new FileStream(path2, FileMode.Open, FileAccess.Read))
                {
                    byte[] buffer1 = new byte[bufferSize];
                    byte[] buffer2 = new byte[bufferSize];
                    int bytesRead1;
                    while ((bytesRead1 = fs1.Read(buffer1, 0, bufferSize)) > 0)
                    {
                        int bytesRead2 = fs2.Read(buffer2, 0, bufferSize);
                        if (bytesRead1 != bytesRead2) return false;
                        for (int i = 0; i < bytesRead1; i++)
                        {
                            if (buffer1[i] != buffer2[i]) return false;
                        }
                    }
                }
                return true;
            }
            catch { return false; }
        }

        private async void PreviewFiles()
        {
            if (_coreSettings.SourceFolders.Count == 0) return;

            // Log via WebView
            await webView.ExecuteScriptAsync("addLog('=== ANTEPRIMA ===');");

            var files = await Task.Run(() => GetFiles());
            var grp = files.GroupBy(GetCategory).OrderByDescending(g => g.Count());

            foreach (var g in grp)
            {
               await webView.ExecuteScriptAsync($"addLog('  {g.Key}: {g.Count()}');");
            }
             await webView.ExecuteScriptAsync($"addLog('Totale: {files.Count} file');");
        }

        private async void OrganizeFiles()
        {
            Log("🚀 Avvio Ottimizzazione...");
            sessionMoved.Clear();
            sessionDeleted.Clear();

            // Initialize run tracking
            _currentRunId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_") + Guid.NewGuid().ToString("N")[..8];
            _dirsEmptiedThisRun.Clear();

            if (_coreSettings.SourceFolders == null || _coreSettings.SourceFolders.Count == 0)
            {
                Log("⚠️ Nessuna cartella selezionata da ottimizzare. Trascina una cartella o clicca 'Sfoglia' nella zona di rilascio.");
                Invoke(new Action(() => {
                    using (var d = new FolderBrowserDialog())
                    {
                        d.Description = "Seleziona la cartella da organizzare";
                        if (d.ShowDialog() == DialogResult.OK)
                        {
                            if (!_coreSettings.SourceFolders.Contains(d.SelectedPath))
                            {
                                _coreSettings.SourceFolders.Add(d.SelectedPath);
                                SaveSettings();
                                UpdateSourceUI();
                                Log($"Cartella aggiunta: {d.SelectedPath}");
                            }
                        }
                        else return;
                    }
                }));
            }

            if (string.IsNullOrWhiteSpace(_coreSettings.LastDestinationFolder))
            {
                Log("⚠️ Destinazione non impostata. Seleziona la cartella dove vuoi spostare i file.");
                Invoke(new Action(() => {
                    using (var d = new FolderBrowserDialog())
                    {
                        d.Description = "Scegli dove salvare i file organizzati";
                        if (d.ShowDialog() == DialogResult.OK)
                        {
                            _coreSettings.LastDestinationFolder = d.SelectedPath;
                            SaveSettings();
                            Log($"Destinazione impostata a: {_coreSettings.LastDestinationFolder}");
                        }
                        else return;
                    }
                }));
            }

            string dest = _coreSettings.LastDestinationFolder;
            if (string.IsNullOrEmpty(dest)) return;

            try { if (!Directory.Exists(dest)) Directory.CreateDirectory(dest); } catch (Exception ex) { Log($"Errore creazione destinazione: {ex.Message}"); return; }

            var files = GetFiles();
            Log($"Trovati {files.Count} file totali da elaborare.");

            if (files.Count == 0)
            {
                Log("Nessun file trovato nelle cartelle selezionate.");
                await webView.ExecuteScriptAsync("addLog('⚠️ Nessun file da organizzare.');");
                return;
            }

            cancellationTokenSource = new System.Threading.CancellationTokenSource();

            int ok = 0, err = 0;
            bool copy = false;
            var token = cancellationTokenSource.Token;

            Log("Inizio elaborazione...");

            await Task.Run(async () =>
            {
                int total = files.Count;
                int lastPercent = -1;

                for (int i = 0; i < total && !token.IsCancellationRequested; i++)
                {
                    int currentPercent = (int)((long)i * 100 / total);
                    if (currentPercent != lastPercent)
                    {
                        lastPercent = currentPercent;
                        // Avoid over-saturating the message queue
                        webView.ExecuteScriptAsync($"updateProgress({currentPercent}, 'Elaborazione...')");
                    }

                    if (OrganizeFile(files[i], copy, token)) ok++; else err++;
                }

                if (_coreSettings.DeleteEmptyFolders && !copy)
                {
                    foreach (var src in _coreSettings.SourceFolders)
                        _safeFile.DeleteEmptyDirs(src, _dirsEmptiedThisRun);
                }
            });

            webView.ExecuteScriptAsync($"updateProgress(100, 'Completato')");
            await webView.ExecuteScriptAsync($"addLog('{T("complete")}: {ok} {T("files")}, {err} {T("errors")}');");
            PlayCompletionSound();

            SaveSettings();

            await Task.Delay(2000);
            Invoke(new Action(() => {
                UpdateUIStats();
                webView.ExecuteScriptAsync($"updateProgress(100, 'Ottimizzato')");
            }));
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
                    Log($"  {(success ? "✓" : "✗")} {Path.GetFileName(op.Source)}: {msg}");
            });
            Log("✅ Annullamento completato.");
            _currentRunId = string.Empty;
        }

        private void Log(string m)
        {
            try
            {
                if (webView != null && !webView.IsDisposed)
                {
                    BeginInvoke(new Action(() => {
                        if (webView.CoreWebView2 != null)
                        {
                            var escaped = m.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\r\n", " ").Replace("\n", " ");
                            webView.CoreWebView2.ExecuteScriptAsync($"addLog('{escaped}');");
                        }
                    }));
                }
            }
            catch { }
        }

        private Task PostMessage(string type, string payload)
        {
            if (webView?.CoreWebView2 == null) return Task.CompletedTask;
            var json = System.Text.Json.JsonSerializer.Serialize(new { type, payload });
            return webView.CoreWebView2.ExecuteScriptAsync($"handleMessage({json})");
        }

        private bool OrganizeFile(string f, bool copy, System.Threading.CancellationToken token)
        {
            if (token.IsCancellationRequested) return false;

            // Safety checks
            if (_safeFile.ShouldSkipFile(f)) return true; // skip silently

            var cat = GetCategory(f);
            try
            {
                // Use settings or passed destination
                var dest = Path.Combine(_coreSettings.LastDestinationFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop), cat);

                // Check for protected path
                if (_safeFile.IsProtectedPath(f))
                {
                    Log($"  ⚠️ Percorso protetto, saltato: {Path.GetFileName(f)}");
                    return true;
                }

                // Check destination inside source
                foreach (var src in _coreSettings.SourceFolders)
                {
                    if (_safeFile.IsDestinationInsideSource(src, dest))
                    {
                        Log($"  ⚠️ Destinazione dentro la sorgente, saltato: {Path.GetFileName(f)}");
                        return true;
                    }
                }

                Directory.CreateDirectory(dest);
                var name = Path.GetFileName(f);
                var target = Path.Combine(dest, name);

                // Check same path
                if (_safeFile.IsSamePath(f, target)) return true;

                if (File.Exists(target))
                {
                    if (AreFilesIdentical(f, target))
                    {
                        if (copy)
                        {
                            Log($"  Skipped (Duplicate): {name}");
                            return true;
                        }
                        else
                        {
                            try
                            {
                                if (_coreSettings.DeleteDuplicates)
                                {
                                    _safeFile.RecycleFile(f);
                                    sessionDeleted.Add(f);
                                    _journal.AppendEntry(new CosmOrganizer.Core.FileOperation(_currentRunId, DateTime.UtcNow, "recycled", f, target));
                                    _dirsEmptiedThisRun.Add(Path.GetDirectoryName(f)!);
                                    Log($"  Recycled (Duplicate): {name}");
                                }
                                else
                                {
                                    Log($"  Skipped (Duplicate, keep): {name}");
                                }
                                return true;
                            }
                            catch { return false; }
                        }
                    }
                }

                int c = 1;

                // Compression Logic
                bool shouldCompress = _coreSettings.CompressOldFiles
                    && (DateTime.Now - File.GetLastWriteTime(f)).TotalDays > 90
                    && new FileInfo(f).Length >= 10485760
                    && !_safeFile.IsAlreadyCompressed(Path.GetExtension(f));

                if (shouldCompress)
                {
                     var zipTarget = Path.Combine(dest, Path.GetFileNameWithoutExtension(name) + ".zip");
                     while (File.Exists(zipTarget)) zipTarget = Path.Combine(dest, $"{Path.GetFileNameWithoutExtension(name)} ({c++}).zip");

                     using (var archive = ZipFile.Open(zipTarget, ZipArchiveMode.Create))
                     {
                         archive.CreateEntryFromFile(f, name);
                     }

                     if (!copy)
                     {
                         _safeFile.RecycleFile(f);
                         _dirsEmptiedThisRun.Add(Path.GetDirectoryName(f)!);
                     }

                     sessionMoved.Add(new { Name = name, Old = f, New = zipTarget });
                     _journal.AppendEntry(new CosmOrganizer.Core.FileOperation(_currentRunId, DateTime.UtcNow, "compressed", f, zipTarget));

                     Log($"  COMPRESSO: {name} -> {cat}");
                     return true;
                }
                else
                {
                    while (File.Exists(target)) target = Path.Combine(dest, $"{Path.GetFileNameWithoutExtension(name)} ({c++}){Path.GetExtension(name)}");

                    if (copy)
                    {
                        File.Copy(f, target);
                        sessionMoved.Add(new { Name = name, Old = f, New = target });
                        _journal.AppendEntry(new CosmOrganizer.Core.FileOperation(_currentRunId, DateTime.UtcNow, "copied", f, target));
                    }
                    else
                    {
                        _safeFile.MoveFile(f, target);
                        _dirsEmptiedThisRun.Add(Path.GetDirectoryName(f)!);
                        sessionMoved.Add(new { Name = name, Old = f, New = target });
                        _journal.AppendEntry(new CosmOrganizer.Core.FileOperation(_currentRunId, DateTime.UtcNow, "moved", f, target));
                    }
                    Log($"  OK: {name} -> {cat}");
                    return true;
                }
            }
            catch (Exception ex) { Log($"  ERR: {ex.Message}"); return false; }
        }

        private void UpdateWatchers()
        {
            _realTime?.Dispose();
            _realTime = null;

            if (_coreSettings.SourceFolders.Count > 0 && isRealTimeEnabled)
            {
                _realTime = new RealTimeService(_safeFile);
                _realTime.FileReady += OnFileReady;
                _realTime.ScanRequired += msg => Log($"⚠️ Re-scan richiesto: {msg}");
                foreach (var folder in _coreSettings.SourceFolders)
                    _realTime.Watch(folder, _coreSettings.IncludeSubfolders);
                Log("Monitoraggio Real-time Attivo ⚡");
            }
        }

        private void OnFileReady(string path)
        {
            if (string.IsNullOrWhiteSpace(_coreSettings.LastDestinationFolder)) return;
            // Run OrganizeFile on UI thread
            this.BeginInvoke(new Action(() => OrganizeFile(path, false, System.Threading.CancellationToken.None)));
        }

        [STAThread]
        static void Main(string[] args)
        {
            VelopackApp.Build().Run();

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                var form = new CosmOrganizerForm();

                if (args.Length > 0 && Directory.Exists(args[0]))
                {
                    form.AddFolderFromArgs(args[0]);
                }

                Application.Run(form);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Errore: {ex.Message}\n\n{ex.StackTrace}", "Errore", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
