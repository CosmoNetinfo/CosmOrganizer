# CosmOrganizer v3.1.0 - Release Notes

## 🎉 Nuove Funzionalità

### ⚡ Monitoraggio Real-time (Magic Folder)
- **Organizzazione automatica**: I file vengono organizzati istantaneamente appena scaricati o copiati nelle cartelle monitorate
- **Controllo intelligente dei file bloccati**: L'app aspetta automaticamente che i download siano completati prima di spostare i file (fino a 30 secondi)
- **Attivazione semplice**: Basta spuntare "Monitoraggio Real-time ⚡" per attivare la funzione

### 👯 Rilevamento Duplicati Intelligente
- **Confronto del contenuto**: Prima di creare copie come `file (1).txt`, l'app verifica se il file è realmente diverso
- **Modalità Copia**: Salta automaticamente i file identici già presenti nella destinazione
- **Modalità Sposta**: Elimina il file sorgente se è identico a quello già presente nella destinazione, mantenendo la cartella pulita

## 🔧 Miglioramenti

- Aggiornata versione a **3.1.0**
- Migliorata la gestione dei file durante il download
- Ottimizzata l'interfaccia utente per una migliore leggibilità
- Corretti problemi di layout con il sottotitolo e la checkbox Real-time

## 📦 Installazione

1. Scarica `CosmOrganizer_Setup_v3.1.0.exe`
2. Esegui l'installer
3. L'applicazione verrà aggiunta al menu contestuale di Windows

## 🆕 Come usare le nuove funzionalità

### Monitoraggio Real-time
1. Apri CosmOrganizer
2. Aggiungi le cartelle da monitorare (es. Download)
3. Imposta la cartella di destinazione
4. Spunta "Monitoraggio Real-time ⚡"
5. Scarica un file e guardalo organizzarsi automaticamente!

### Rilevamento Duplicati
La funzione è **sempre attiva** e funziona sia con l'organizzazione manuale che con il monitoraggio real-time.

## 🐛 Bug Fix

- Risolto: Errore durante il download di file con monitoraggio attivo
- Risolto: Problemi di visualizzazione del sottotitolo nell'header
- Risolto: Logo mancante dopo la build

## 📝 Note Tecniche

- Richiede .NET 8.0 Runtime (incluso nell'installer)
- Compatibile con Windows 10 e Windows 11
- Supporto completo per il menu contestuale moderno di Windows 11

---

**Versione precedente**: 3.0.0  
**Data di rilascio**: 30 Novembre 2024  
**Sviluppatore**: CosmoNet.info
