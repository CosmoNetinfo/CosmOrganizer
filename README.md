# CosmOrganizer 🌌

**CosmOrganizer** è l'assistente per Windows che mantiene in perfetto ordine il tuo PC (Download, Desktop, Documenti e qualsiasi cartella a scelta) in modo intelligente, sicuro e completamente locale. Nessun account richiesto, nessuna telemetria e nessun dato inviato al cloud.

![CosmOrganizer](logo.png)

---

## ⚡ Download e Installazione

Scarica sempre l'ultima versione stabile tramite i link ufficiali diretti:

- 📦 **Installer con Auto-Aggiornamenti (consigliato):**  
  👉 [CosmOrganizer-win-Setup.exe](https://github.com/CosmoNetinfo/CosmOrganizer/releases/latest/download/CosmOrganizer-win-Setup.exe)
- 🧰 **Versione Portable (senza installazione):**  
  👉 [CosmOrganizer-win-Portable.zip](https://github.com/CosmoNetinfo/CosmOrganizer/releases/latest/download/CosmOrganizer-win-Portable.zip)
- 📋 **Tutte le versioni e note di rilascio:**  
  👉 [Releases su GitHub](https://github.com/CosmoNetinfo/CosmOrganizer/releases)

### Requisiti di sistema
- **Windows 10 / Windows 11 (64-bit)**
- **Nessun prerequisito**: l'applicazione è **self-contained** (.NET 8 non deve essere installato separatamente). L'installer include e verifica automaticamente anche il runtime Microsoft Edge WebView2.

> [!NOTE]
> **Avviso Windows SmartScreen**: CosmOrganizer è un software open source gratuito privo di certificato di firma commerciale a pagamento. Al primo avvio Windows potrebbe mostrare la schermata blu di protezione: è sufficiente fare clic su **"Ulteriori informazioni"** e poi su **"Esegui comunque"**.

> [!IMPORTANT]
> **Aggiornamento da CosmOrganizer v3.x**: se hai installato una versione precedente con il vecchio installer Inno Setup, disinstallala da *Impostazioni > App installate* di Windows prima di installare la versione 4.0. Da questo momento in poi, tutti i futuri aggiornamenti saranno gestiti automaticamente e in background da Velopack.

---

## 🚀 Novità e Caratteristiche Principali (v4.0)

- 🛡️ **Architettura Sicura "Zero Perdite"**:
  - Nessuna eliminazione permanente: i file rimossi finiscono sempre nel **Cestino di Windows** (`SHFileOperation`).
  - Spostamento cross-drive affidabile: verifica dell'integrità tramite **hash SHA-256** prima della cancellazione della sorgente.
  - Cartelle protette di sistema e file sensibili (`.lnk`, `.url`, download parziali `.crdownload`, desktop.ini) rigorosamente ignorati.
- 🗂️ **Profili e Modalità di Smistamento**:
  - Profili dedicati con cartelle sorgente e destinazioni indipendenti.
  - Smistamento per **Categoria** (`categories.json` modificabile dall'utente), **Anno/Mese**, **Estensione** o regole su misura.
  - Anteprima *(Dry-Run)* prima di applicare qualsiasi modifica ai file.
- ⏱️ **Monitoraggio Real-Time e Pianificazione**:
  - Rileva nuovi file scaricati con coda asincrona debounced (attesa completamento download e rilascio lock).
  - Pianificazione integrata: esecuzione a intervalli orari, ad orario fisso giornaliero, all'avvio o al superamento di una soglia di file.
- 🔍 **Strumenti Avanzati per lo Spazio**:
  - **Trova Duplicati**: scansione progressiva ad alte prestazioni (dimensione → partial hash 64KB → SHA-256 completo). Mai cancellazione accidentale di tutte le copie.
  - **Analizzatore di Spazio**: visualizzazione istantanea delle cartelle e dei file più pesanti.
  - **Pulizia Installer Vecchi**: riconosce versioni multiple di eseguibili d'installazione (`discordsetup-1.0.exe`, `discordsetup-2.0.exe`) e mantiene solo la versione più recente.
  - **Manutenzione File Temporanei**: individua e sposta nel cestino file a 0-byte, residui `.tmp` e lock file orfani (`~$`).
  - **Ordinamento Media & Screenshot**: estrazione data originale EXIF da foto JPG/PNG e instradamento automatico degli screenshot del desktop.
  - **Rinomina Batch**: modelli con contatore, pulizia suffissi `(1)` o `Copia di` e rimozione caratteri speciali.
- ⏪ **Giornale JSONL e "Dov'è finito?"**:
  - Ogni operazione viene registrata atomicamente in file JSONL per esecuzione.
  - Funzione **Annulla (Ctrl+Z)** completa con conferma per ripristinare i file nella posizione originaria.
  - Motore di ricerca storico per ritrovare al volo dove è stato spostato un file.
- 🖱️ **Integrazione Esplora Risorse (Shell)**:
  - Tasto destro su qualsiasi cartella per visualizzare `"Organizza con CosmOrganizer"` (registrazione in HKCU, nessun privilegio di amministratore richiesto).
- 🎨 **Interfaccia Moderna e 100% Offline**:
  - Icone Phosphor e font Outfit inclusi localmente: nessun CDN esterno, funzionamento garantito anche senza connessione internet.
  - Supporto Dark/Light mode e scorciatoie rapide da tastiera.

---

## ⌨️ Scorciatoie da Tastiera

| Scorciatoia | Azione |
| :--- | :--- |
| <kbd>Ctrl</kbd> + <kbd>Z</kbd> | Richiede conferma e annulla l'ultima operazione di organizzazione |
| <kbd>Ctrl</kbd> + <kbd>O</kbd> | Apre la finestra per selezionare una cartella sorgente da aggiungere |

---

## 💡 Esempi di Regole Pratiche

Puoi personalizzare le regole di smistamento direttamente dalla scheda **Regole** dell'interfaccia:

### 1. Archiviazione Foto e Video per Anno e Mese
- **Condizione**: Estensione appartiene a `jpg, jpeg, png, heic, mp4, mov`
- **Azione**: Sposta nella cartella `Media\{YYYY}\{MM}`
- **Risultato**: `IMG_20261015.jpg` viene organizzato automaticamente in `Media\2026\10\IMG_20261015.jpg`.

### 2. Smistamento Automatico Screenshot dal Desktop
- **Condizione**: Nome file contiene `Screenshot` oppure `Cattura`
- **Azione**: Sposta nella cartella `Immagini\Screenshot`
- **Risultato**: Mantiene il Desktop pulito trasferendo istantaneamente le schermate catturate.

### 3. Isolamento Documenti di Lavoro e Fatture
- **Condizione**: Nome file contiene `Fattura` O `Contratto` E estensione è `pdf`
- **Azione**: Sposta nella cartella `Documenti\Amministrazione`
- **Risultato**: Riconoscimento immediato dei documenti fiscali prioritari.

---

## ❓ Domande Frequenti (FAQ)

<details>
<summary><strong>1. Dove vengono salvati i dati, i profili e le impostazioni?</strong></summary>
<br>
Tutti i dati dell'applicazione sono conservati localmente sul tuo computer nella cartella dell'utente:
<code>%APPDATA%\CosmOrganizer</code> (ad esempio <code>C:\Users\&lt;TuoUtente&gt;\AppData\Roaming\CosmOrganizer</code>). I file di registro e il giornale delle operazioni sono salvati in formato JSONL trasparente e leggibile.
</details>

<details>
<summary><strong>2. Come funziona l'annullamento (Undo)? Posso annullare anche dopo aver riavviato il PC?</strong></summary>
<br>
Sì! Il giornale delle operazioni tiene traccia di ogni singolo file spostato o compresso con il suo percorso originale e la data di esecuzione. Premendo il pulsante <strong>Annulla</strong> o la combinazione <kbd>Ctrl</kbd>+<kbd>Z</kbd>, CosmOrganizer rilegge l'ultimo run e riposiziona con precisione i file al loro posto di origine.
</details>

<details>
<summary><strong>3. I miei file possono andare persi o essere eliminati per sbaglio?</strong></summary>
<br>
No. La filosofia di progettazione di CosmOrganizer v4.0 è "Zero Perdite":
<ul>
  <li>Nessuna chiamata a <code>File.Delete</code> diretto per i documenti utente: ogni eliminazione (duplicati, junk, installer obsoleti) passa dal <strong>Cestino di Windows</strong>.</li>
  <li>Nei trasferimenti tra dischi diversi (ad esempio da <code>C:</code> a <code>D:</code>), la rimozione del file originale avviene esclusivamente dopo aver verificato con successo che l'hash SHA-256 della destinazione corrisponde al bit con la sorgente.</li>
  <li>I file temporanei dei browser (come <code>.crdownload</code> o <code>.part</code>) vengono automaticamente ignorati finché il download non è completato al 100%.</li>
</ul>
</details>

<details>
<summary><strong>4. Come posso disattivare o personalizzare gli aggiornamenti automatici?</strong></summary>
<br>
Dalla schermata <strong>Impostazioni</strong> dell'applicazione, puoi disattivare in qualsiasi momento l'interruttore <em>Aggiornamenti automatici</em>. Puoi comunque verificare la presenza di nuove versioni facendo clic su <em>"Controlla aggiornamenti ora"</em>.
</details>

<details>
<summary><strong>5. Come si disinstalla CosmOrganizer?</strong></summary>
<br>
È sufficiente aprire <em>Impostazioni di Windows > App > App installate</em>, cercare <strong>CosmOrganizer</strong> e fare clic su <strong>Disinstalla</strong>. La disinstallazione pulisce automaticamente i collegamenti, le chiavi del menu contestuale di Esplora Risorse e l'avvio automatico con Windows.
</details>

---

## 🛠️ Contribuire e Segnalazioni

Se riscontri un problema o hai un'idea per una nuova funzionalità:
- Apri una segnalazione su **[GitHub Issues](https://github.com/CosmoNetinfo/CosmOrganizer/issues)**.
- Consulta la documentazione interna in [`docs/ARCHITETTURA.md`](docs/ARCHITETTURA.md).

---

© 2026 **CosmoNet.info** — Distribuito con licenza [MIT](LICENSE).
