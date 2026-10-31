# Architettura di CosmOrganizer v4.0

CosmOrganizer è progettato secondo principi di modularità, sicurezza "Zero Perdite" ed esecuzione al 100% locale senza telemetria né cloud.

```
┌────────────────────────────────────────────────────────┐
│                   CosmOrganizer.App                    │
│  WinForms Host + Microsoft Edge WebView2 (HTML/CSS/JS) │
└──────────────────────────┬─────────────────────────────┘
                           │ WebMessage JSON Bridge (postMessage)
┌──────────────────────────▼─────────────────────────────┐
│                   CosmOrganizer.Core                   │
│   Headless .NET 8 Library (Zero Windows Forms / UI)    │
│                                                        │
│  • SafeFileService      • OrganizationService          │
│  • RuleEngine           • ProfileService               │
│  • DuplicateFinder      • StorageAnalyzerService       │
│  • ArchiveService       • InstallerCleanerService      │
│  • MediaSorterService   • BatchRenamerService          │
│  • MaintenanceService   • FolderHealthService          │
│  • RealTimeService      • JournalService & Search      │
└────────────────────────────────────────────────────────┘
```

---

## 1. Struttura dei Moduli

### `CosmOrganizer.Core` (Libreria Headless)
- Indipendente dall'interfaccia utente: contiene tutti i modelli di dati, motori di regole e servizi logici.
- **`SafeFileService`**: incapsula le operazioni sul file system. Implementa lo spostamento cross-drive con validazione SHA-256 e l'eliminazione esclusiva tramite Cestino di Windows (`SHFileOperation`). Impedisce l'accesso o la manipolazione di cartelle di sistema e file sensibili (`.lnk`, `.url`, download temporanei `.crdownload`).
- **`JournalService`**: scrive le registrazioni delle operazioni in formato JSONL per garantire la tracciabilità e la reversibilità completa di ogni run.
- **`RuleEngine`**: valuta le condizioni logiche (AND/OR, estensioni, tag, regex) e determina le cartelle di destinazione.

### `CosmOrganizer.App` (Host Desktop)
- Finestra WinForms ad alta risoluzione con dark mode nativa Windows 11 (`DwmSetWindowAttribute`).
- Hosting del runtime WebView2 con memoria isolata in `%LOCALAPPDATA%\CosmOrganizer\WebView2`.
- Fallback nativo: se il runtime WebView2 non è installato, mostra un pannello informativo WinForms con collegamento per il download diretto, prevenendo crash anomali.
- Asset offline (font Outfit e icone Phosphor) inclusi localmente senza alcuna richiesta HTTP verso CDN.

---

## 2. Ponte WebMessage (JSON Bridge)

La comunicazione bidirezionale tra l'host C# e il frontend HTML/JS avviene tramite il protocollo di messaggi WebView2:

1. **Frontend → Backend (`postMessage`)**:
   - Comandi semplici: `'organize'`, `'undo'`, `'preview'`, `'findDuplicates'`, `'scanJunk'`, `'checkUpdates'`.
   - Comandi con payload: `'set:activeProfile:<name>'`, `'applyPreview:<json>'`, `'recycleDuplicates:<json>'`.
2. **Backend → Frontend (`PostMessage / ExecuteScriptAsync`)**:
   - Eventi di stato: `{ type: "stats", payload: { ... } }`.
   - Dati elaborati: `{ type: "previewData", payload: [...] }`, `{ type: "duplicatesData", payload: [...] }`.
   - Log e notifiche: `{ type: "log", payload: "..." }`.

---

## 3. Giornale delle Operazioni e Undo

Ogni esecuzione che sposta o archivia file genera un identificatore univoco `RunId` (`run_<timestamp>_<guid>`).
- File: `%APPDATA%\CosmOrganizer\journal\<RunId>.jsonl`.
- Struttura riga: `FileOperation` immutabile con timestamp, tipo operazione (`Move`, `ArchiveZip`, `Recycle`), percorso originale, percorso destinazione e dimensione.
- **Ripristino (Undo)**: `JournalService.UndoRun(runId, safeFile)` legge le righe in ordine cronologico inverso e riporta ciascun file alla sua posizione originale, scompattando gli archivi o ripristinando i percorsi.

---

## 4. Pipeline CI/CD e Aggiornamenti

- **GitHub Actions (`.github/workflows/build.yml`)**:
  - Si attiva ad ogni push su `main`.
  - Compila i progetti in modalità `Release` con self-contained per architettura `win-x64`.
  - Esegue l'intera suite di test unitari (`dotnet test`).
  - Utilizza **Velopack CLI** (`vpk pack`) per impacchettare l'applicazione generando l'installer autonomo (`CosmOrganizer-Setup.exe`) e il pacchetto portable (`CosmOrganizer-Portable.zip`).
  - Crea o aggiorna una GitHub Release pubblicando gli asset firmati e i file delta per gli auto-aggiornamenti automatici in background.
