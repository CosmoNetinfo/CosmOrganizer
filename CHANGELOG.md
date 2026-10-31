# Cronologia delle versioni (Changelog)

Tutte le modifiche rilevanti a CosmOrganizer sono documentate in questo file.

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
