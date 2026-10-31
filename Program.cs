using System;
using System.IO;
using System.Windows.Forms;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Media;
using System.Net.Http;
using System.Diagnostics;
using Microsoft.Win32;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CosmOrganizer
{
    public class CosmOrganizerForm : Form
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("Gdi32.dll", EntryPoint = "CreateRoundRectRgn")]
        private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;

        private void ApplyRoundedCorners()
        {
            try
            {
                // Set Region for actual clipping
                // Using 12px radius (diameter 24)
                int r = 24;
                this.Region = System.Drawing.Region.FromHrgn(CreateRoundRectRgn(0, 0, Width + 1, Height + 1, r, r));

                // Win11 native rounding attribute
                int attribute = DWMWCP_ROUND;
                DwmSetWindowAttribute(this.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref attribute, sizeof(int));
            }
            catch { }
        }

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
        private List<FileOperation> operationHistory = new List<FileOperation>();
        private List<object> sessionMoved = new List<object>();
        private List<string> sessionDeleted = new List<string>();
        private System.Threading.CancellationTokenSource cancellationTokenSource;
        private Dictionary<string, Dictionary<string, string>> translations;
        private string currentLanguage = "it";
        private const string APP_VERSION = "3.2.0";
        private const string UPDATE_CHECK_URL = "https://api.github.com/repos/CosmoNetinfo/CosmOrganizer/releases/latest";
        
        // Real-time Monitoring
        private CheckBox chkRealTime;
        private bool isRealTimeEnabled = false;
        private List<FileSystemWatcher> watchers = new List<FileSystemWatcher>();
        
        private AppSettings settings = new AppSettings();

        private class FileOperation { public string SourcePath, DestPath; public bool WasCopied; }

        public class AppSettings
        {
            public bool IsDarkMode { get; set; } = true;
            public string Language { get; set; } = "it";
            public bool ScheduleEnabled { get; set; }
            public DateTime ScheduledTime { get; set; }
            public bool ScheduleDaily { get; set; }
            public List<string> ExcludedExtensions { get; set; } = new List<string>();
            public List<string> ExcludedFolders { get; set; } = new List<string>();
            public List<string> SourceFolders { get; set; } = new List<string>();
            public long MinFileSize { get; set; } = 0;
            public long MaxFileSize { get; set; } = long.MaxValue;
            public bool RealTimeEnabled { get; set; }
            public bool DeleteEmptyFolders { get; set; }
            public bool CompressOldFiles { get; set; }
            public bool IncludeSubfolders { get; set; } = false; // Default false
            public int CompressDays { get; set; } = 90;
            public long MinCompressSize { get; set; } = 10485760; // Default 10MB
            
            // Statistics
            public long TotalFilesOrganized { get; set; }
            public long TotalFilesCompressed { get; set; }
            public long TotalFilesDeleted { get; set; }
            public long TotalSpaceSavedMB { get; set; }
            public DateTime StatsSince { get; set; } = DateTime.Now;
            
            // UI State
            public string LastDestinationFolder { get; set; }
        }
        
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
            
            this.Resize += (s, e) => ApplyRoundedCorners();
            
            // 🔄 Check aggiornamenti
            Task.Run(() => CheckForUpdates());
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyRoundedCorners();
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
                if (!settings.SourceFolders.Contains(folderPath))
                {
                    settings.SourceFolders.Add(folderPath);
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
        
        private async Task CheckForUpdates()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("CosmOrganizer");
                    client.Timeout = TimeSpan.FromSeconds(5);
                    
                    var response = await client.GetStringAsync(UPDATE_CHECK_URL);
                    var tagStart = response.IndexOf("\"tag_name\":\"") + 12;
                    
                    if (tagStart > 11)
                    {
                        var tagEnd = response.IndexOf("\"", tagStart);
                        if (tagEnd > tagStart)
                        {
                            var latestVersion = response.Substring(tagStart, tagEnd - tagStart).TrimStart('v').Trim();
                            
                            // Confronto versioni: verifica che sia valida e diversa
                            if (!string.IsNullOrEmpty(latestVersion) && 
                                latestVersion != APP_VERSION && 
                                IsNewerVersion(latestVersion, APP_VERSION))
                            {
                                Invoke(new Action(() =>
                                {
                                    var result = MessageBox.Show(
                                        $"È disponibile una nuova versione: {latestVersion}\nVersione attuale: {APP_VERSION}\n\nVuoi scaricare l'aggiornamento?",
                                        "Aggiornamento Disponibile",
                                        MessageBoxButtons.YesNo,
                                        MessageBoxIcon.Information
                                    );
                                    
                                    if (result == DialogResult.Yes)
                                    {
                                        Process.Start(new ProcessStartInfo
                                        {
                                            FileName = "https://github.com/CosmoNetinfo/CosmOrganizer/releases/latest",
                                            UseShellExecute = true
                                        });
                                    }
                                }));
                            }
                        }
                    }
                }
            }
            catch { }
        }
        
        private bool IsNewerVersion(string latest, string current)
        {
            try
            {
                var latestParts = latest.Split('.').Select(int.Parse).ToArray();
                var currentParts = current.Split('.').Select(int.Parse).ToArray();
                
                for (int i = 0; i < Math.Min(latestParts.Length, currentParts.Length); i++)
                {
                    if (latestParts[i] > currentParts[i]) return true;
                    if (latestParts[i] < currentParts[i]) return false;
                }
                
                return latestParts.Length > currentParts.Length;
            }
            catch
            {
                return false; // In caso di errore, non mostrare l'alert
            }
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
        
        private string T(string k) => translations.ContainsKey(settings.Language) && translations[settings.Language].ContainsKey(k) ? translations[settings.Language][k] : k;
        
        private async void InitializeWebView()
        {
            // Borderless Modern Window Setup
            Text = "CosmOrganizer";
            FormBorderStyle = FormBorderStyle.None; // Remove default Windows borders
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
            webView.NavigationCompleted += (s, e) => UpdateUIStats();
            
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
                        if (Directory.Exists(path) && !settings.SourceFolders.Contains(path))
                        {
                            settings.SourceFolders.Add(path);
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
            var list = string.Join(", ", settings.SourceFolders.Select(p => Path.GetFileName(p)));
            if (string.IsNullOrEmpty(list)) list = "Trascina qui le cartelle";
            webView.ExecuteScriptAsync($"if(document.querySelector('#dropZone h3')) document.querySelector('#dropZone h3').innerText = '{list.Replace("'", "\\'")}';");
        }

        private void UpdateUIStats()
        {
            if (webView == null || webView.IsDisposed) return;
            
            Invoke(new Action(async () => {
                if (webView.CoreWebView2 == null) return;

                // Calculate Stats
                long fileCount = settings.TotalFilesOrganized;
                long deletedCount = settings.TotalFilesDeleted;
                string spaceSaved = (settings.TotalSpaceSavedMB < 1024) 
                    ? $"{settings.TotalSpaceSavedMB:F1} MB" 
                    : $"{(settings.TotalSpaceSavedMB/1024f):F2} GB";
                bool isMon = settings.RealTimeEnabled;
                string dest = string.IsNullOrEmpty(settings.LastDestinationFolder) ? "Non selezionata" : settings.LastDestinationFolder;

                var js = $@"
                    if(typeof updateStats === 'function') updateStats({fileCount}, '{spaceSaved}', {isMon.ToString().ToLower()}, {deletedCount});
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
                        if (chkDelete) chkDelete.checked = {settings.DeleteEmptyFolders.ToString().ToLower()};
                        if (chkCompress) chkCompress.checked = {settings.CompressOldFiles.ToString().ToLower()};
                        if (chkSub) chkSub.checked = {settings.IncludeSubfolders.ToString().ToLower()};
                    }}, 100);
                ";
                await webView.ExecuteScriptAsync(jsCode);

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
                    settings.DeleteEmptyFolders = val == "true";
                    SaveSettings();
                }
                else if (msg.StartsWith("set:compress:"))
                {
                    var val = msg.Substring("set:compress:".Length);
                    settings.CompressOldFiles = val == "true";
                    SaveSettings();
                }
                else if (msg.StartsWith("set:deleteEmpty:"))
                {
                    var val = msg.Substring("set:deleteEmpty:".Length);
                    settings.DeleteEmptyFolders = val == "true";
                    SaveSettings();
                }
                else if (msg.StartsWith("set:startup:"))
                {
                    var val = msg.Substring("set:startup:".Length);
                    SetStartupRegistry(val == "true");
                }
                else if (msg.StartsWith("set:includeSub:"))
                {
                    var val = msg.Substring("set:includeSub:".Length);
                    settings.IncludeSubfolders = val == "true";
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
                               if (!settings.SourceFolders.Contains(d.SelectedPath)) 
                               {
                                   settings.SourceFolders.Add(d.SelectedPath);
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
                               settings.LastDestinationFolder = d.SelectedPath;
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
            settings.RealTimeEnabled = !settings.RealTimeEnabled;
            isRealTimeEnabled = settings.RealTimeEnabled;
            SaveSettings();
            UpdateWatchers();
            Log(settings.RealTimeEnabled ? "Monitoraggio Attivato ⚡" : "Monitoraggio Disattivato");
        }

        private void InitializeUI()
        {
            // Legacy UI initialization is suppressed in favor of InitializeWebView
        }
        
        private void ApplyTheme()
        {
            if (settings.IsDarkMode)
            {
                BackColor = Color.FromArgb(26, 29, 41);
                headerPanel.BackColor = Color.FromArgb(36, 40, 55);
                ForeColor = Color.FromArgb(248, 250, 252);
                
                foreach (Control c in Controls)
                {
                    if (c is Label) c.ForeColor = Color.FromArgb(248, 250, 252);
                    if (c is ListBox) { c.BackColor = Color.FromArgb(26, 29, 41); c.ForeColor = Color.FromArgb(248, 250, 252); }
                    if (c is TextBox && c != txtLog) { c.BackColor = Color.FromArgb(26, 29, 41); c.ForeColor = Color.FromArgb(248, 250, 252); }
                    if (c is CheckBox) c.ForeColor = Color.FromArgb(248, 250, 252);
                    if (c is ComboBox) { c.BackColor = Color.FromArgb(26, 29, 41); c.ForeColor = Color.FromArgb(248, 250, 252); }
                }
                
                txtLog.BackColor = Color.FromArgb(15, 15, 20);
                txtLog.ForeColor = Color.FromArgb(16, 185, 129);
                
                foreach (Control c in headerPanel.Controls)
                {
                    if (c is Label) c.ForeColor = Color.FromArgb(248, 250, 252);
                    if (c is CheckBox) c.ForeColor = Color.FromArgb(248, 250, 252);
                }
            }
            else
            {
                BackColor = Color.FromArgb(248, 250, 252);
                headerPanel.BackColor = Color.White;
                ForeColor = Color.FromArgb(30, 41, 59);
                
                foreach (Control c in Controls)
                {
                    if (c is Label) c.ForeColor = Color.FromArgb(30, 41, 59);
                    if (c is ListBox) { c.BackColor = Color.White; c.ForeColor = Color.FromArgb(30, 41, 59); }
                    if (c is TextBox && c != txtLog) { c.BackColor = Color.White; c.ForeColor = Color.FromArgb(30, 41, 59); }
                    if (c is CheckBox) c.ForeColor = Color.FromArgb(30, 41, 59);
                    if (c is ComboBox) { c.BackColor = Color.White; c.ForeColor = Color.FromArgb(30, 41, 59); }
                }
                
                txtLog.BackColor = Color.FromArgb(30, 41, 59);
                txtLog.ForeColor = Color.FromArgb(16, 185, 129);
                
                foreach (Control c in headerPanel.Controls)
                {
                    if (c is Label) c.ForeColor = Color.FromArgb(30, 41, 59);
                    if (c is CheckBox) c.ForeColor = Color.FromArgb(30, 41, 59);
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
            foreach (string d in settings.SourceFolders)
            {
                if (!Directory.Exists(d)) 
                {
                    Log($"Cartella non trovata: {d}");
                    continue;
                }
                try
                {
                    var opts = settings.IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly; 
                    var found = Directory.GetFiles(d, "*.*", opts);
                    if (found.Length == 0) Log($"Nessun file trovato in: {d}");
                    
                    foreach (var file in found)
                    {
                        var fi = new FileInfo(file);
                        if (settings.ExcludedExtensions.Contains(fi.Extension.ToLower())) continue;
                        if (settings.ExcludedFolders.Any(ef => file.Contains(ef))) continue;
                        if (fi.Length < settings.MinFileSize || fi.Length > settings.MaxFileSize) continue;
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
                var txtExt = new TextBox { Location = new Point(15, 40), Size = new Size(400, 25), Text = string.Join(", ", settings.ExcludedExtensions) };
                var lblDir = new Label { Text = T("excludeDir"), Location = new Point(15, 80), AutoSize = true };
                var txtDir = new TextBox { Location = new Point(15, 105), Size = new Size(400, 25), Text = string.Join(", ", settings.ExcludedFolders) };
                var lblMin = new Label { Text = T("minSize"), Location = new Point(15, 145), AutoSize = true };
                var numMin = new NumericUpDown { Location = new Point(15, 170), Size = new Size(180, 25), Maximum = 10000, Value = (decimal)(settings.MinFileSize / 1048576) };
                var lblMax = new Label { Text = T("maxSize"), Location = new Point(235, 145), AutoSize = true };
                var numMax = new NumericUpDown { Location = new Point(235, 170), Size = new Size(180, 25), Maximum = 100000, Value = settings.MaxFileSize == long.MaxValue ? 0 : (decimal)(settings.MaxFileSize / 1048576) };
                
                var btnSave = new Button { Text = T("save"), Location = new Point(150, 230), Size = new Size(130, 40), FlatStyle = FlatStyle.Flat };
                btnSave.Click += (ss, ee) =>
                {
                    settings.ExcludedExtensions = txtExt.Text.Split(',').Select(x => x.Trim().ToLower()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                    settings.ExcludedFolders = txtDir.Text.Split(',').Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                    settings.MinFileSize = (long)(numMin.Value * 1048576);
                    settings.MaxFileSize = numMax.Value == 0 ? long.MaxValue : (long)(numMax.Value * 1048576);
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
                var chkEnable = new CheckBox { Text = T("schedEnable"), Location = new Point(15, 20), AutoSize = true, Checked = settings.ScheduleEnabled };
                var lblTime = new Label { Text = T("schedTime"), Location = new Point(15, 60), AutoSize = true };
                var dtpTime = new DateTimePicker { Location = new Point(15, 85), Size = new Size(350, 25), Format = DateTimePickerFormat.Time, ShowUpDown = true, Value = settings.ScheduledTime == DateTime.MinValue ? DateTime.Now : settings.ScheduledTime };
                var chkDaily = new CheckBox { Text = T("schedDaily"), Location = new Point(15, 125), AutoSize = true, Checked = settings.ScheduleDaily };
                
                var btnSave = new Button { Text = T("save"), Location = new Point(120, 170), Size = new Size(130, 40), FlatStyle = FlatStyle.Flat };
                btnSave.Click += (ss, ee) =>
                {
                    settings.ScheduleEnabled = chkEnable.Checked;
                    settings.ScheduledTime = dtpTime.Value;
                    settings.ScheduleDaily = chkDaily.Checked;
                    SaveSettings();
                    dlg.Close();
                };
                
                dlg.Controls.AddRange(new Control[] { chkEnable, lblTime, dtpTime, chkDaily, btnSave });
                dlg.ShowDialog();
            }
        }
        

        private void SaveSettings()
        {
            try
            {
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CosmOrganizer_v3.json"), json);
            }
            catch { }
        }
        
        private void LoadSettings()
        {
            try
            {
                var cfg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CosmOrganizer_v3.json");
                if (File.Exists(cfg))
                {
                    settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(cfg));
                    if (settings.SourceFolders == null) settings.SourceFolders = new List<string>();
                }
                else
                {
                    // Migrazione vecchie settings se esistono
                    var oldCfg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CosmOrganizer.cfg");
                    if (File.Exists(oldCfg)) {
                         var p = File.ReadAllText(oldCfg).Split('|');
                         if (p.Length >= 5) {
                            settings.IsDarkMode = bool.Parse(p[0]);
                            settings.Language = p[1];
                            settings.ScheduleEnabled = bool.Parse(p[2]);
                            settings.ScheduledTime = DateTime.Parse(p[3]);
                            settings.ScheduleDaily = bool.Parse(p[4]);
                            if (p.Length > 5) settings.ExcludedExtensions = p[5].Split(',').Where(x => !string.IsNullOrEmpty(x)).ToList();
                            if (p.Length > 6) settings.ExcludedFolders = p[6].Split(',').Where(x => !string.IsNullOrEmpty(x)).ToList();
                            if (p.Length > 7) settings.MinFileSize = long.Parse(p[7]);
                            if (p.Length > 8) settings.MaxFileSize = long.Parse(p[8]);
                            if (p.Length > 9) settings.RealTimeEnabled = bool.Parse(p[9]);
                         }
                    }
                }
                UpdateWatchers();
            }
            catch { settings = new AppSettings(); }
        }
        
        private string GetCategory(string f)
        {
            var e = Path.GetExtension(f).ToLower(); 
            
            string[] imgs = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".svg", ".webp", ".ico", ".tiff", ".raw", ".heic", ".psd" };
            string[] vids = { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg", ".3gp" };
            string[] arch = { ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".iso", ".dmg" };
            string[] apps = { ".exe", ".msi", ".bat", ".cmd", ".sh", ".app", ".deb", ".rpm" };

            if (imgs.Contains(e)) return "Immagini"; 
            if (vids.Contains(e)) return "Video"; 
            if (arch.Contains(e)) return "Compressi"; 
            if (apps.Contains(e)) return "Applicazioni"; 
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
            if (settings.SourceFolders.Count == 0) return; 
            
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
            
            if (settings.SourceFolders == null || settings.SourceFolders.Count == 0) 
            {
                Log("⚠️ Nessuna cartella selezionata da ottimizzare. Trascina una cartella o clicca 'Sfoglia' nella zona di rilascio.");
                Invoke(new Action(() => {
                    using (var d = new FolderBrowserDialog())
                    {
                        d.Description = "Seleziona la cartella da organizzare";
                        if (d.ShowDialog() == DialogResult.OK)
                        {
                            if (!settings.SourceFolders.Contains(d.SelectedPath))
                            {
                                settings.SourceFolders.Add(d.SelectedPath);
                                SaveSettings();
                                UpdateSourceUI();
                                Log($"Cartella aggiunta: {d.SelectedPath}");
                            }
                        }
                        else return;
                    }
                }));
            }

            if (string.IsNullOrWhiteSpace(settings.LastDestinationFolder)) 
            {
                Log("⚠️ Destinazione non impostata. Seleziona la cartella dove vuoi spostare i file.");
                Invoke(new Action(() => {
                    using (var d = new FolderBrowserDialog())
                    {
                        d.Description = "Scegli dove salvare i file organizzati";
                        if (d.ShowDialog() == DialogResult.OK)
                        {
                            settings.LastDestinationFolder = d.SelectedPath;
                            SaveSettings();
                            Log($"Destinazione impostata a: {settings.LastDestinationFolder}");
                        }
                        else return;
                    }
                }));
            }
            
            string dest = settings.LastDestinationFolder;
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
            
            operationHistory.Clear(); 
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

                if (settings.DeleteEmptyFolders && !copy)
                {
                    foreach (var src in settings.SourceFolders)
                    {
                         if (Directory.Exists(src)) DeleteEmptyDirs(src);
                    }
                }
            });
            
            webView.ExecuteScriptAsync($"updateProgress(100, 'Completato')");
            await webView.ExecuteScriptAsync($"addLog('{T("complete")}: {ok} {T("files")}, {err} {T("errors")}');");
            PlayCompletionSound();
            
            // Aggiorna statistiche nel database e nella UI
            settings.TotalFilesOrganized += ok;
            SaveSettings();
            
            await Task.Delay(2000);
            Invoke(new Action(() => {
                UpdateUIStats();
                webView.ExecuteScriptAsync($"updateProgress(100, 'Ottimizzato')");
            }));
        }
        
        private async void UndoOperations()
        {
            if (operationHistory.Count == 0) 
            {
                Log("⚠️ Nessuna operazione recente da annullare.");
                return;
            }
            
            Log("🔄 Ripristino file in corso...");
            
            int ok = 0;
            int total = operationHistory.Count;
            
            await Task.Run(async () => 
            { 
                int total = operationHistory.Count;
                int lastPercent = -1;

                // Restore in reverse order
                for (int i = total - 1; i >= 0; i--) 
                { 
                    int currentPercent = (int)((long)(total - 1 - i) * 100 / total);
                    if (currentPercent != lastPercent)
                    {
                        lastPercent = currentPercent;
                        webView.ExecuteScriptAsync($"updateProgress({currentPercent}, 'Ripristino...')");
                    }

                    var op = operationHistory[i]; 
                    try 
                    { 
                        if (File.Exists(op.DestPath)) 
                        { 
                            string dir = Path.GetDirectoryName(op.SourcePath);
                            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); 
                            
                            File.Move(op.DestPath, op.SourcePath); 
                            Log($"  Ripristinato: {Path.GetFileName(op.SourcePath)}");
                            ok++; 
                        } 
                    } 
                    catch (Exception ex) { Log($"  ERRORE Ripristino: {ex.Message}"); } 
                } 
            });
            
            webView.ExecuteScriptAsync($"updateProgress(100, 'Ripristinato')");
            Log($"✅ Ripristino completato: {ok} file riportati alla posizione originale.");
            PlayCompletionSound();
            
            operationHistory.Clear(); 
            
            await Task.Delay(2000);
            webView.ExecuteScriptAsync($"updateProgress(0, 'Ottimizzato')");
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

        private bool OrganizeFile(string f, bool copy, System.Threading.CancellationToken token)
        {
            if (token.IsCancellationRequested) return false;
            var cat = GetCategory(f);
            try
            {
                // Use settings or passed destination
                var dest = Path.Combine(settings.LastDestinationFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop), cat);
                Directory.CreateDirectory(dest);
                var name = Path.GetFileName(f);
                var target = Path.Combine(dest, name);

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
                                File.Delete(f);
                                sessionDeleted.Add(f);
                                settings.TotalFilesDeleted++;
                                operationHistory.Add(new FileOperation { SourcePath = f, DestPath = target, WasCopied = false });
                                Log($"  Deleted source (Duplicate): {name}");
                                return true;
                            }
                            catch { return false; }
                        }
                    }
                }

                int c = 1;
                
                // Compression Logic
                bool shouldCompress = settings.CompressOldFiles 
                    && (DateTime.Now - File.GetLastWriteTime(f)).TotalDays > settings.CompressDays
                    && new FileInfo(f).Length >= settings.MinCompressSize;

                if (shouldCompress)
                {
                     var zipTarget = Path.Combine(dest, Path.GetFileNameWithoutExtension(name) + ".zip");
                     while (File.Exists(zipTarget)) zipTarget = Path.Combine(dest, $"{Path.GetFileNameWithoutExtension(name)} ({c++}).zip");

                     using (var archive = ZipFile.Open(zipTarget, ZipArchiveMode.Create))
                     {
                         archive.CreateEntryFromFile(f, name);
                     }
                     
                     if (!copy) File.Delete(f);
                     
                     sessionMoved.Add(new { Name = name, Old = f, New = zipTarget });
                     operationHistory.Add(new FileOperation { SourcePath = f, DestPath = zipTarget, WasCopied = copy });
                     
                     settings.TotalFilesCompressed++;
                     settings.TotalSpaceSavedMB += (new FileInfo(zipTarget).Length / 1024 / 1024); // Approximation

                     Log($"  COMPRESSO: {name} -> {cat}");
                     return true;
                }
                else
                {
                    while (File.Exists(target)) target = Path.Combine(dest, $"{Path.GetFileNameWithoutExtension(name)} ({c++}){Path.GetExtension(name)}");
                    
                    if (copy) File.Copy(f, target); else File.Move(f, target);
                    sessionMoved.Add(new { Name = name, Old = f, New = target });
                    operationHistory.Add(new FileOperation { SourcePath = f, DestPath = target, WasCopied = copy });
                    Log($"  OK: {name} -> {cat}");
                    return true;
                }
            }
            catch (Exception ex) { Log($"  ERR: {ex.Message}"); return false; }
        }

        private void DeleteEmptyDirs(string startDir)
        {
            try
            {
                foreach (var d in Directory.GetDirectories(startDir))
                {
                    DeleteEmptyDirs(d);
                }
                
                var entries = Directory.GetFileSystemEntries(startDir);
                if (entries.Length == 0)
                {
                    Directory.Delete(startDir, false);
                    Log($"  Removed empty dir: {startDir}");
                }
            }
            catch { }
        }

        private void UpdateWatchers()
        {
            foreach (var w in watchers) { w.EnableRaisingEvents = false; w.Dispose(); }
            watchers.Clear();

            if (settings.RealTimeEnabled)
            {
                foreach (string path in settings.SourceFolders)
                {
                    if (Directory.Exists(path))
                    {
                        var w = new FileSystemWatcher(path);
                        w.IncludeSubdirectories = settings.IncludeSubfolders;
                        w.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite;
                        w.Created += OnFileDetected;
                        w.Renamed += OnFileDetected;
                        w.EnableRaisingEvents = true;
                        watchers.Add(w);
                    }
                }
                Log("Real-time Monitoring Active ⚡");
            }
            else
            {
                Log("Real-time Monitoring Disabled");
            }
        }

        private async void OnFileDetected(object sender, FileSystemEventArgs e)
        {
            // Wait for file to be fully written and unlocked
            if (!await WaitForFileReady(e.FullPath, maxWaitSeconds: 30)) return;
            
            try
            {
                var fi = new FileInfo(e.FullPath);
                if (settings.ExcludedExtensions.Contains(fi.Extension.ToLower())) return;
                if (settings.ExcludedFolders.Any(ef => e.FullPath.Contains(ef))) return;
                if (fi.Length < settings.MinFileSize || fi.Length > settings.MaxFileSize) return;

                // Check if destination is set
                if (string.IsNullOrWhiteSpace(settings.LastDestinationFolder)) return;
                
                OrganizeFile(e.FullPath, false, System.Threading.CancellationToken.None); // Default to move
            }
            catch { }
        }

        private async Task<bool> WaitForFileReady(string filePath, int maxWaitSeconds)
        {
            int attempts = 0;
            int maxAttempts = maxWaitSeconds * 2; // Check every 500ms
            
            while (attempts < maxAttempts)
            {
                try
                {
                    if (!File.Exists(filePath)) return false;
                    
                    // Try to open the file exclusively to check if it's locked
                    using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                        return true; // File is ready
                    }
                }
                catch (IOException)
                {
                    // File is still locked, wait and retry
                    await Task.Delay(500);
                    attempts++;
                }
                catch
                {
                    return false; // Other error, skip this file
                }
            }
            
            return false; // Timeout
        }
        
        [STAThread]
        static void Main(string[] args)
        {
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
