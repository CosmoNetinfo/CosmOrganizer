// CosmOrganizer Interface Client Script
// © CosmoNet.info — MIT License

let currentLang = localStorage.getItem('cosmo_lang') || 'it';
let translations = {};
let previewData = [];
let duplicatesData = [];
let junkData = [];
let installersData = [];

// Notify backend when DOM is ready
document.addEventListener('DOMContentLoaded', async () => {
    console.log('DOM fully loaded');
    await loadTranslations(currentLang);
    applyThemeFromStorage();
    const selLang = document.getElementById('selLang');
    if (selLang) selLang.value = currentLang;

    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.addEventListener('message', event => {
            if (event.data) handleMessage(event.data);
        });

// Keyboard Shortcuts & Function Keys
window.addEventListener('keydown', (e) => {
    // Esc: close modal if open
    if (e.key === 'Escape') {
        const modal = document.getElementById('modal-container');
        if (modal && modal.style.display !== 'none') {
            closeModal();
            return;
        }
    }
    // Ctrl + Z: Undo
    if (e.ctrlKey && (e.key === 'z' || e.key === 'Z')) {
        e.preventDefault();
        confirmUndo();
        return;
    }
    // Ctrl + O: Add / Browse folder
    if (e.ctrlKey && (e.key === 'o' || e.key === 'O')) {
        e.preventDefault();
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.postMessage('browse');
        }
        return;
    }
    // F1: Help
    if (e.key === 'F1') {
        e.preventDefault();
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.postMessage('help');
        }
        return;
    }
    // F5: Refresh UI / Stats
    if (e.key === 'F5') {
        e.preventDefault();
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.postMessage('refresh');
        }
        return;
    }
});
        window.chrome.webview.postMessage('pageReady');
    }
});

// --- i18n Engine ---
async function loadTranslations(lang) {
    try {
        const res = await fetch(`i18n/${lang}.json`);
        if (res.ok) {
            translations = await res.json();
            currentLang = lang;
            applyTranslations();
        }
    } catch (e) {
        console.warn('Could not load translations:', e);
    }
}

function applyTranslations() {
    document.querySelectorAll('[data-i18n]').forEach(el => {
        const key = el.getAttribute('data-i18n');
        if (translations[key]) {
            el.textContent = translations[key];
        }
    });
}

function setLanguage(lang) {
    localStorage.setItem('cosmo_lang', lang);
    const sel = document.getElementById('selLang');
    if (sel) sel.value = lang;
    loadTranslations(lang);
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage('set:language:' + lang);
    }
}

// --- Theme Engine ---
function setTheme(theme) {
    localStorage.setItem('cosmo_theme', theme);
    applyTheme(theme);
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage('set:theme:' + theme);
    }
}

function applyTheme(theme) {
    if (theme === 'light') {
        document.body.classList.add('theme-light');
    } else if (theme === 'system') {
        const isDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
        document.body.classList.toggle('theme-light', !isDark);
    } else {
        document.body.classList.remove('theme-light');
    }
    const sel = document.getElementById('selTheme');
    if (sel) sel.value = theme;
}

function applyThemeFromStorage() {
    const saved = localStorage.getItem('cosmo_theme') || 'dark';
    applyTheme(saved);
}

// --- Navigation Logic ---
function showView(viewId) {
    const views = ['dashboard', 'duplicates', 'storage', 'clean', 'history', 'rules', 'preview', 'settings', 'logs'];
    const navItems = document.querySelectorAll('.nav-item');
    
    // 1. Update navigation items
    navItems.forEach(nav => nav.classList.remove('active'));
    const index = views.indexOf(viewId);
    if (index !== -1 && navItems[index]) {
        navItems[index].classList.add('active');
    }

    // 2. Hide all view sections and remove .active class
    document.querySelectorAll('.view-section').forEach(el => {
        el.classList.remove('active');
        el.style.display = 'none';
    });
    
    // 3. Show target view section and add .active class
    const targetView = document.getElementById('view-' + viewId);
    if (targetView) {
        targetView.classList.add('active');
        targetView.style.display = 'flex';
    }
    
    // 4. Update page title
    const pageTitle = document.getElementById('pageTitle');
    if (pageTitle) {
        const titleKeys = {
            dashboard: 'systemDashboard',
            duplicates: 'systemDuplicates',
            storage: 'systemStorage',
            clean: 'systemClean',
            history: 'systemHistory',
            rules: 'systemRules',
            preview: 'systemPreview',
            settings: 'systemSettings',
            logs: 'systemLogs'
        };
        const key = titleKeys[viewId];
        pageTitle.textContent = translations[key] || ('System / ' + viewId);
    }
}


// Drag & Drop visual feedback
const dropZone = document.getElementById('dropZone');
if (dropZone) {
    dropZone.addEventListener('dragover', (e) => { e.preventDefault(); dropZone.style.borderColor = 'var(--neon-green)'; });
    dropZone.addEventListener('dragleave', (e) => { e.preventDefault(); dropZone.style.borderColor = ''; });
    dropZone.addEventListener('drop', (e) => { e.preventDefault(); dropZone.style.borderColor = ''; });
}

// --- Profiles & Modes ---
function onProfileChanged(profileName) {
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage('set:activeProfile:' + profileName);
    }
}

function onModeChanged(mode) {
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage('set:organizeMode:' + mode);
    }
}

function populateProfiles(profiles, activeName) {
    const sel = document.getElementById('profileSelect');
    if (!sel) return;
    sel.innerHTML = '';
    profiles.forEach(p => {
        const opt = document.createElement('option');
        opt.value = p.Name || p;
        opt.textContent = p.Name || p;
        if (opt.value === activeName) opt.selected = true;
        sel.appendChild(opt);
    });
}

// --- Preview Engine ---
function requestPreview() {
    showView('preview');
    const tbody = document.getElementById('previewBody');
    if (tbody) {
        tbody.innerHTML = '<tr><td colspan="4" style="text-align: center; color: var(--neon-cyan);">Analisi e simulazione in corso...</td></tr>';
    }
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage('preview');
    }
}

function renderPreview(items) {
    previewData = items || [];
    const tbody = document.getElementById('previewBody');
    if (!tbody) return;
    tbody.innerHTML = '';

    if (previewData.length === 0) {
        tbody.innerHTML = '<tr><td colspan="4" style="text-align: center; color: var(--text-secondary);">Nessun file trovato da organizzare nel profilo attivo.</td></tr>';
        return;
    }

    previewData.forEach((item, idx) => {
        const tr = document.createElement('tr');
        
        const tdCheck = document.createElement('td');
        const chk = document.createElement('input');
        chk.type = 'checkbox';
        chk.checked = !item.Excluded;
        chk.onchange = () => { item.Excluded = !chk.checked; };
        tdCheck.appendChild(chk);

        const tdSrc = document.createElement('td');
        tdSrc.textContent = item.SourcePath;
        tdSrc.style.wordBreak = 'break-all';

        const tdDst = document.createElement('td');
        tdDst.textContent = item.DestPath;
        tdDst.style.wordBreak = 'break-all';
        tdDst.style.color = 'var(--neon-green)';

        const tdAct = document.createElement('td');
        tdAct.textContent = item.RuleOrCategory;

        tr.appendChild(tdCheck);
        tr.appendChild(tdSrc);
        tr.appendChild(tdDst);
        tr.appendChild(tdAct);
        tbody.appendChild(tr);
    });
}

function toggleSelectAllPreview(checked) {
    const tbody = document.getElementById('previewBody');
    if (!tbody) return;
    tbody.querySelectorAll('input[type="checkbox"]').forEach(chk => {
        chk.checked = checked;
    });
    previewData.forEach(p => p.Excluded = !checked);
}

function applyPreviewSelected() {
    const included = previewData.filter(p => !p.Excluded).map(p => p.SourcePath);
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage('applyPreview:' + JSON.stringify(included));
    }
}

// --- Duplicates View ---
function scanDuplicates() {
    const container = document.getElementById('duplicatesList');
    if (container) container.innerHTML = '<div style="text-align:center; color: var(--neon-cyan); padding:20px;">Scansione duplicati in corso...</div>';
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage('findDuplicates');
    }
}

function renderDuplicates(groups) {
    duplicatesData = groups || [];
    const container = document.getElementById('duplicatesList');
    if (!container) return;
    container.innerHTML = '';

    if (duplicatesData.length === 0) {
        container.innerHTML = '<div style="text-align:center; color: var(--text-secondary); padding:20px;">Nessun duplicato trovato nelle cartelle del profilo attivo!</div>';
        return;
    }

    duplicatesData.forEach((grp, gIdx) => {
        const box = document.createElement('div');
        box.className = 'glass';
        box.style.padding = '12px';
        box.style.marginBottom = '12px';
        box.style.borderRadius = '8px';

        const sizeMb = (grp.FileSizeBytes / 1048576).toFixed(2);
        box.innerHTML = `<div style="font-weight:600; color:var(--neon-cyan); margin-bottom:8px;">Gruppo ${gIdx + 1} (${grp.Files.length} copie - ${sizeMb} MB ciascuna)</div>`;

        grp.Files.forEach((f, fIdx) => {
            const row = document.createElement('div');
            row.style.display = 'flex';
            row.style.alignItems = 'center';
            row.style.gap = '10px';
            row.style.padding = '4px 0';

            const chk = document.createElement('input');
            chk.type = 'checkbox';
            chk.checked = f.SelectedForRecycle;
            chk.onchange = () => { f.SelectedForRecycle = chk.checked; };

            const pathSpan = document.createElement('span');
            pathSpan.style.flex = '1';
            pathSpan.style.fontSize = '0.85rem';
            pathSpan.textContent = f.Path;

            const dateSpan = document.createElement('span');
            dateSpan.style.fontSize = '0.75rem';
            dateSpan.style.color = 'var(--text-secondary)';
            dateSpan.textContent = new Date(f.LastModified).toLocaleDateString();

            row.appendChild(chk);
            row.appendChild(pathSpan);
            row.appendChild(dateSpan);
            box.appendChild(row);
        });

        container.appendChild(box);
    });
}

function recycleDuplicates() {
    if (duplicatesData.length === 0) return;
    if (confirm("Inviare i file duplicati selezionati nel Cestino di Windows?")) {
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.postMessage('recycleDuplicates:' + JSON.stringify(duplicatesData));
        }
    }
}

// --- Storage Analyzer View ---
function analyzeStorage() {
    const container = document.getElementById('storageReport');
    if (container) container.innerHTML = '<div style="text-align:center; color:var(--neon-cyan); padding:20px;">Analisi dello spazio disco in corso...</div>';
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage('analyzeStorage');
    }
}

function renderStorage(res) {
    const container = document.getElementById('storageReport');
    if (!container || !res) return;
    container.innerHTML = '';

    const totalMb = (res.TotalBytes / 1048576).toFixed(1);
    const summaryHeader = document.createElement('div');
    summaryHeader.innerHTML = `<h3>Totale: ${totalMb} MB in ${res.TotalFiles} file</h3>`;
    container.appendChild(summaryHeader);

    // Subdirectories bars
    if (res.HeaviestSubdirectories && res.HeaviestSubdirectories.length > 0) {
        const subSec = document.createElement('div');
        subSec.innerHTML = `<h4 style="margin: 15px 0 8px 0; color:var(--neon-cyan);">Cartelle più pesanti</h4>`;
        res.HeaviestSubdirectories.forEach(sub => {
            const barBox = document.createElement('div');
            barBox.style.marginBottom = '8px';
            const sizeStr = (sub.TotalBytes / 1048576).toFixed(1) + ' MB';
            barBox.innerHTML = `
                <div style="display:flex; justify-content:space-between; font-size:0.85rem;">
                    <span>${sub.Name} (${sub.FileCount} file)</span>
                    <span>${sizeStr} (${sub.PercentageOfTotal}%)</span>
                </div>
                <div style="background: rgba(255,255,255,0.08); border-radius:4px; height:8px; overflow:hidden; margin-top:2px;">
                    <div style="background:var(--neon-purple); width:${sub.PercentageOfTotal}%; height:100%;"></div>
                </div>
            `;
            subSec.appendChild(barBox);
        });
        container.appendChild(subSec);
    }

    // Top 10 Largest files
    if (res.TopLargestFiles && res.TopLargestFiles.length > 0) {
        const topSec = document.createElement('div');
        topSec.innerHTML = `<h4 style="margin: 15px 0 8px 0; color:var(--neon-cyan);">I file più grandi</h4>`;
        const ul = document.createElement('ul');
        ul.style.listStyle = 'none';
        ul.style.padding = '0';
        res.TopLargestFiles.slice(0, 10).forEach(f => {
            const li = document.createElement('li');
            li.style.padding = '6px 0';
            li.style.borderBottom = '1px solid rgba(255,255,255,0.05)';
            li.style.display = 'flex';
            li.style.justifyContent = 'space-between';
            li.style.fontSize = '0.85rem';
            li.innerHTML = `<span>${f.Name}</span><span style="color:var(--neon-cyan);">${f.FormattedSize}</span>`;
            ul.appendChild(li);
        });
        topSec.appendChild(ul);
        container.appendChild(topSec);
    }
}

// --- Cleanup View ---
function scanJunk() {
    const container = document.getElementById('cleanupList');
    if (container) container.innerHTML = '<div style="text-align:center; color:var(--neon-cyan); padding:20px;">Ricerca file spazzatura in corso...</div>';
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage('scanJunk');
    }
}

function scanInstallers() {
    const container = document.getElementById('cleanupList');
    if (container) container.innerHTML = '<div style="text-align:center; color:var(--neon-cyan); padding:20px;">Ricerca installer vecchi in corso...</div>';
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage('cleanInstallers');
    }
}

function renderJunk(items) {
    junkData = items || [];
    const container = document.getElementById('cleanupList');
    if (!container) return;
    container.innerHTML = '';

    if (junkData.length === 0) {
        container.innerHTML = '<div style="text-align:center; color:var(--text-secondary); padding:20px;">Nessun file temporaneo o vuoto rilevato!</div>';
        return;
    }

    const header = document.createElement('div');
    header.style.display = 'flex';
    header.style.justifyContent = 'space-between';
    header.style.marginBottom = '12px';
    header.innerHTML = `<span>Trovati <b>${junkData.length}</b> file spazzatura</span><button class="btn-action" onclick="recycleJunk()"><i class="ph ph-trash"></i> Cestina Selezionati</button>`;
    container.appendChild(header);

    junkData.forEach(item => {
        const row = document.createElement('div');
        row.className = 'glass';
        row.style.padding = '8px 12px';
        row.style.marginBottom = '6px';
        row.style.display = 'flex';
        row.style.alignItems = 'center';
        row.style.gap = '10px';

        const chk = document.createElement('input');
        chk.type = 'checkbox';
        chk.checked = item.SelectedForRecycle;
        chk.onchange = () => { item.SelectedForRecycle = chk.checked; };

        const span = document.createElement('span');
        span.style.flex = '1';
        span.style.fontSize = '0.85rem';
        span.textContent = `${item.Path} (${item.Reason})`;

        row.appendChild(chk);
        row.appendChild(span);
        container.appendChild(row);
    });
}

function recycleJunk() {
    if (junkData.length === 0) return;
    if (confirm("Spostare i file spazzatura selezionati nel Cestino?")) {
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.postMessage('recycleJunk:' + JSON.stringify(junkData));
        }
    }
}

function renderInstallers(items) {
    installersData = items || [];
    const container = document.getElementById('cleanupList');
    if (!container) return;
    container.innerHTML = '';

    if (installersData.length === 0) {
        container.innerHTML = '<div style="text-align:center; color:var(--text-secondary); padding:20px;">Nessun installer vecchio o duplicato rilevato.</div>';
        return;
    }

    const header = document.createElement('div');
    header.style.display = 'flex';
    header.style.justifyContent = 'space-between';
    header.style.marginBottom = '12px';
    header.innerHTML = `<span>Rilevati <b>${installersData.length}</b> pacchetti installer</span><button class="btn-action" onclick="recycleInstallers()"><i class="ph ph-trash"></i> Cestina Selezionati</button>`;
    container.appendChild(header);

    installersData.forEach(item => {
        const row = document.createElement('div');
        row.className = 'glass';
        row.style.padding = '8px 12px';
        row.style.marginBottom = '6px';
        row.style.display = 'flex';
        row.style.alignItems = 'center';
        row.style.gap = '10px';

        const chk = document.createElement('input');
        chk.type = 'checkbox';
        chk.checked = item.SelectedForRecycle;
        chk.onchange = () => { item.SelectedForRecycle = chk.checked; };

        const tag = item.IsOlderVersion ? '<span style="color:#ff6464; font-size:0.75rem;">[Versione Vecchia]</span>' : '';
        const span = document.createElement('span');
        span.style.flex = '1';
        span.style.fontSize = '0.85rem';
        span.innerHTML = `${item.FileName} ${tag} <span style="color:var(--text-secondary); font-size:0.75rem;">(${new Date(item.LastModified).toLocaleDateString()})</span>`;

        row.appendChild(chk);
        row.appendChild(span);
        container.appendChild(row);
    });
}

function recycleInstallers() {
    if (installersData.length === 0) return;
    if (confirm("Spostare gli installer selezionati nel Cestino?")) {
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.postMessage('recycleInstallers:' + JSON.stringify(installersData));
        }
    }
}

// --- History & "Where did it go?" View ---
function searchHistoryFile() {
    const q = document.getElementById('txtSearchHistory')?.value || '';
    if (!q) return;
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage('searchJournal:' + q);
    }
}

function renderHistory(results) {
    const container = document.getElementById('historyResults');
    if (!container) return;
    container.innerHTML = '';

    if (!results || results.length === 0) {
        container.innerHTML = '<div style="text-align:center; color:var(--text-secondary); padding:20px;">Nessun record trovato per la ricerca indicata nel journal delle esecuzioni.</div>';
        return;
    }

    results.forEach(res => {
        const card = document.createElement('div');
        card.className = 'glass';
        card.style.padding = '10px 14px';
        card.style.marginBottom = '8px';
        card.style.borderRadius = '8px';

        const statusIcon = res.DestinationStillExists ? '<i class="ph-fill ph-check-circle" style="color:var(--neon-green);"></i>' : '<i class="ph-fill ph-warning" style="color:#ffb800;"></i>';
        const dateStr = new Date(res.Timestamp).toLocaleString();

        card.innerHTML = `
            <div style="display:flex; justify-content:space-between; font-weight:600; margin-bottom:4px;">
                <span>${statusIcon} ${res.FileName} (${res.Kind})</span>
                <span style="font-size:0.75rem; color:var(--text-secondary);">${dateStr}</span>
            </div>
            <div style="font-size:0.8rem; color:var(--text-secondary);">Da: ${res.OriginalSource}</div>
            <div style="font-size:0.8rem; color:var(--neon-green);">A: ${res.FinalDestination || '(Cestino)'}</div>
        `;
        container.appendChild(card);
    });
}

// --- Rules Engine UI ---
function renderRules(rules) {
    const container = document.getElementById('rulesList');
    if (!container) return;
    container.innerHTML = '';

    if (!rules || rules.length === 0) {
        container.innerHTML = '<div style="color: var(--text-secondary);">Nessuna regola definita per questo profilo.</div>';
        return;
    }

    rules.forEach((rule, idx) => {
        const card = document.createElement('div');
        card.className = 'rule-card glass';

        const info = document.createElement('div');
        info.className = 'rule-info';

        const name = document.createElement('div');
        name.className = 'rule-name';
        name.textContent = `${rule.Name || 'Regola'} (${rule.Action})`;

        const detail = document.createElement('div');
        detail.className = 'rule-detail';
        detail.textContent = `Destinazione: ${rule.DestinationTemplate || '{categoria}'}`;

        info.appendChild(name);
        info.appendChild(detail);

        const actions = document.createElement('div');
        actions.className = 'rule-actions';

        const toggle = document.createElement('input');
        toggle.type = 'checkbox';
        toggle.checked = rule.Enabled;
        toggle.title = 'Attiva / Disattiva';
        toggle.onchange = () => {
            if (window.chrome && window.chrome.webview) {
                window.chrome.webview.postMessage(`toggleRule:${rule.Id}:${toggle.checked}`);
            }
        };

        const testBtn = document.createElement('button');
        testBtn.className = 'btn-browse';
        testBtn.innerHTML = '<i class="ph ph-flask"></i> Prova';
        testBtn.title = 'Prova questa regola su un file';
        testBtn.onclick = () => {
            if (window.chrome && window.chrome.webview) {
                window.chrome.webview.postMessage(`testRule:${rule.Id}`);
            }
        };

        actions.appendChild(toggle);
        actions.appendChild(testBtn);

        card.appendChild(info);
        card.appendChild(actions);
        container.appendChild(card);
    });
}

// --- C# Bridge Message Handler ---
function handleMessage(msg) {
    if (!msg) return;
    const type = msg.type || msg.Type;
    const payload = msg.payload !== undefined ? msg.payload : msg.Payload;

    switch (type) {
        case 'stats':
            if (payload) {
                updateStats(payload.files, payload.space, payload.isMonitoring, payload.deletedCount);
            }
            break;
        case 'progress':
            if (payload) {
                updateProgress(payload.percent, payload.label);
            }
            break;
        case 'profiles':
            if (payload && payload.profiles) {
                populateProfiles(payload.profiles, payload.active);
            }
            break;
        case 'previewData':
            renderPreview(payload);
            break;
        case 'rules':
            renderRules(payload);
            break;
        case 'duplicatesData':
            renderDuplicates(payload);
            break;
        case 'storageData':
            renderStorage(payload);
            break;
        case 'junkData':
            renderJunk(payload);
            break;
        case 'installerData':
            renderInstallers(payload);
            break;
        case 'whereDidItGoResults':
            renderHistory(payload);
            break;
        case 'log':
            addLog(payload);
            break;
        case 'toast':
            alert(payload);
            break;
        case 'updateCheckResult':
            const updateResEl = document.getElementById('updateCheckResult');
            if (updateResEl) updateResEl.textContent = payload;
            break;
        case 'appVersion':
            setAppVersion(payload);
            break;
    }
}

function checkUpdatesManual() {
    const resEl = document.getElementById('updateCheckResult');
    if (resEl) resEl.textContent = 'Controllo in corso...';
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage('checkUpdates');
    }
}

// Functions called by C# or legacy calls
function updateProgress(percent, label) {
    const effVal = document.getElementById('efficiencyVal');
    if (effVal) effVal.innerHTML = percent + '<span class="percent">%</span>';
    
    if (label) {
        const heroLabel = document.querySelector('.hero-label');
        if (heroLabel) heroLabel.innerText = label;
    }
}

function updateStats(files, space, isMonitoring, deletedCount) {
    const elFiles = document.getElementById('statFiles');
    if (elFiles) elFiles.innerText = files;
    
    const elDeleted = document.getElementById('statDeleted');
    if (elDeleted) elDeleted.innerText = deletedCount || '0';
    
    const elSpace = document.getElementById('statSpace');
    if (elSpace) elSpace.innerText = space;
    
    const monEl = document.getElementById('statMonitor');
    const iconEl = document.getElementById('iconMonitor');
    
    if (monEl) {
        monEl.innerText = isMonitoring ? 'ON' : 'OFF';
        monEl.className = isMonitoring ? 'stat-value text-green pulse-text' : 'stat-value text-secondary';
    }
    
    if (iconEl) {
        iconEl.innerHTML = isMonitoring ? '<i class="ph ph-wifi-high"></i>' : '<i class="ph ph-wifi-slash"></i>';
        iconEl.className = isMonitoring ? 'stat-icon green-bg' : 'stat-icon purple-bg';
    }

    const heroLabel = document.querySelector('.hero-label');
    if (heroLabel && heroLabel.innerText !== 'Elaborazione...') {
        const effVal = document.getElementById('efficiencyVal');
        if (effVal) effVal.innerHTML = isMonitoring ? '100<span class="percent">%</span>' : '0<span class="percent">%</span>';
    }
}

function showDetails(title, filesJson) {
    const modal = document.getElementById('modal-container');
    const titleEl = document.getElementById('modal-title');
    const listEl = document.getElementById('details-list');
    
    if (!modal || !titleEl || !listEl) return;
    
    titleEl.innerText = title;
    listEl.innerHTML = '';
    
    const files = typeof filesJson === 'string' ? JSON.parse(filesJson) : filesJson;
    if (files.length === 0) {
        listEl.innerHTML = '<li>Nessun file presente per questa categoria.</li>';
    } else {
        files.forEach(f => {
            const li = document.createElement('li');
            if (typeof f === 'object') {
                li.innerHTML = `<b>${f.Name || ''}</b><span>Da: ${f.Old || ''}</span><span>A: ${f.New || ''}</span>`;
            } else {
                li.innerHTML = `<b>${f}</b>`;
            }
            listEl.appendChild(li);
        });
    }
    
    modal.style.display = 'flex';
}

function addLog(message) {
    const list = document.getElementById('logList');
    if (list) {
        const li = document.createElement('li');
        li.innerText = new Date().toLocaleTimeString() + " - " + message;
        list.prepend(li);
        if (list.children.length > 50) list.lastChild.remove();
    }
}

function toggleSetting(name, value) {
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage('set:' + name + ':' + value);
    }
}

function setAppVersion(v) {
    const el = document.getElementById('appVersion') || document.querySelector('.version-tag');
    if (el) el.textContent = 'v' + v;
    const settingsEl = document.getElementById('settingsAppVersion');
    if (settingsEl) settingsEl.textContent = 'v' + v;
}

function showUpdateBanner(v) {
    const banner = document.getElementById('updateBanner');
    const txt = document.getElementById('updateBannerText');
    if (banner && txt) {
        txt.textContent = 'Aggiornamento ' + v + ' pronto — Riavvia per installare';
        banner.style.display = 'flex';
    }
}
