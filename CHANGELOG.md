# Cronologia delle versioni (Changelog)

Tutte le modifiche rilevanti a CosmOrganizer sono documentate in questo file.

---

## [4.0.0] - 2026-11-01

Versione milestone di riarchitettura completa con motore a due livelli (.NET 8 Core + WinForms WebView2) e standard "Zero Perdite".

### Aggiunto
- **Architettura Core & App**: separazione netta tra libreria headless (`CosmOrganizer.Core`) e interfaccia utente (`CosmOrganizer.App`).
- **Giornale Operazioni JSONL (`JournalService`)**: registrazione atomica di ogni file elaborato per esecuzione con supporto Undo persistente e affidabile.
- **Motore di Regole Avanzato (`RuleEngine`)**: condizioni combinate (AND/OR), supporto tag temporali (`{YYYY}`, `{MM}`, `{DD}`) e metadati di provenienza (NTFS Zone.Identifier).
- **Profili Indipendenti (`ProfileService`)**: configurazione di profili multipli con cartelle sorgente e destinazione indipendenti; risoluzione percorsi tramite Windows Known Folders API.
- **Rilevamento Duplicati ad Alte Prestazioni (`DuplicateFinderService`)**: scansione progressiva (dimensione -> hash parziale 64 KB -> SHA-256); protezione integrata che garantisce di non eliminare mai l'ultima copia.
- **Analizzatore di Spazio Disco (`StorageAnalyzerService`)**: visualizzazione grafica delle sottocartelle e classifica dei file più pesanti.
- **Pulizia Installer Obsoleti (`InstallerCleanerService`)**: normalizzazione del nome software e rimozione delle versioni precedenti, mantenendo la più recente.
- **Ordinamento Media & EXIF (`MediaSorterService`)**: parsing dei metadati EXIF `DateTimeOriginal` da immagini JPEG/PNG e smistamento screenshot dedicato.
- **Ridenominazione Batch Avanzata (`BatchRenamerService`)**: modelli con contatore sequenziale, rimozione caratteri non validi e pulizia duplicati `(1)` / `Copia di`.
- **Ricerca Storica "Dov'è finito?" (`JournalSearchService`)**: motore di ricerca istantaneo nel giornale storico delle operazioni.
- **Integrazione Esplora Risorse (`WindowsShellIntegration`)**: comando "Organizza con CosmOrganizer" registrato in `HKCU` (senza richiesta di privilegi di amministratore).
- **Funzionamento 100% Offline**: font Outfit e icone Phosphor integrate localmente come asset vendor, eliminando qualsiasi dipendenza da CDN esterni.
- **Aggiornamenti Velopack**: controllo manuale dalla UI, notifica non invasiva e applicazione trasparente alla chiusura.
- **Scorciatoie da Tastiera**: <kbd>Ctrl</kbd>+<kbd>Z</kbd> per annullare l'ultima esecuzione con dialogo di conferma e <kbd>Ctrl</kbd>+<kbd>O</kbd> per aggiungere rapidamente cartelle.

### Modificato
- Riscritto il servizio di monitoraggio in tempo reale (`RealTimeService`) con `System.Threading.Channels`, debounce di 2 secondi e backoff esponenziale per i file bloccati dai browser o da altri processi.
- Integrazione completa del Cestino di Windows (`SafeFileService` con `SHFileOperation` nativo): vietata l'eliminazione permanente diretta per i documenti utente.
- Trasferimento cross-drive sicuro con calcolo e verifica dell'hash SHA-256 prima della rimozione del file originale sulla sorgente.
- Fallback nativo WebView2: in caso di runtime mancante o danneggiato, visualizzazione di un avviso WinForms con link diretto per il download senza interruzione anomala del processo.
- Memorizzazione e ripristino automatico di dimensioni e coordinate della finestra all'avvio su configurazioni multi-monitor.

### Sicurezza
- Protezione totale dei percorsi di sistema (`Windows`, `Program Files`, cartelle utente critiche) e skip automatico di file speciali (`.lnk`, `.url`, download temporanei `.crdownload`, `desktop.ini`).

---

## [3.2.0] - 2026-01-04

### Aggiunto
- **Pulizia automatica cartelle vuote**: rimozione opzionale delle cartelle sorgenti rimaste vuote dopo lo smistamento.
- **Compressione archiviazione**: opzione per comprimere automaticamente file vecchi (> 90 giorni) e pesanti (> 10 MB) in formato `.zip`.
- **Dashboard e statistiche**: contatore file organizzati, file eliminati e spazio risparmiato in tempo reale con visualizzazione grafica ad anello.
- **Modalità Undo ("Torna Indietro")**: ripristino dei file spostati all'ultima posizione originale.
- **Controllo sottocartelle**: opzione per includere o escludere le sottocartelle dalla scansione (disattivata di default).
- **Dettagli operazioni**: finestre modali interattive per ispezionare l'elenco dei file spostati e dei duplicati eliminati.

### Modificato
- Riorganizzazione grafica con interfaccia futuristica glassmorphism basata su WebView2.
- Salvataggio persistente delle cartelle sorgenti e delle preferenze utente.
- Angoli arrotondati nativi per Windows 11.

---

## [3.1.0] - 2024-11-30

### Aggiunto
- **Monitoraggio Real-time (Magic Folder)**: organizzazione automatica istantanea dei file creati o scaricati nelle cartelle monitorate.
- **Controllo intelligente file bloccati**: attesa per il completamento dei download attivi prima dello spostamento.
- **Rilevamento duplicati identici**: verifica dell'effettivo contenuto prima della creazione di file duplicati; rimozione automatica della copia sorgente identica.

### Corretto
- Risolti conflitti di accesso concorrente durante il salvataggio o download di file.
- Correzioni al layout e al rendering dell'header.
