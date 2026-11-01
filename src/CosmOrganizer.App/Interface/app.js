// CosmOrganizer Interface Client Script
// © CosmoNet.info — MIT License

let currentLang = localStorage.getItem('cosmo_lang') || 'it';
let translations = {};
let previewData = [];
let duplicatesData = [];
let junkData = [];
let installersData = [];
let systemDrives = [];
let selectedStorageTarget = '';
let selectedCleanTarget = 'SYSTEM_TEMP';
let currentCleanMode = 'junk';

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
        loadDrives();

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

    if ((viewId === 'storage' || viewId === 'clean') && (!systemDrives || systemDrives.length === 0)) {
        loadDrives();
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

// --- System Drives Management ---
function loadDrives() {
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage('getDrives');
    }
}

function refreshDrives() {
    loadDrives();
}

function renderDrives(drives) {
    systemDrives = drives || [];
    renderStorageDrives();
    renderCleanDrives();
}

function renderStorageDrives() {
    const container = document.getElementById('storageDrivesContainer');
    const select = document.getElementById('storageTargetSelect');
    if (!container || !select) return;

    // 1. Populate Dropdown
    select.innerHTML = '';
    const activeProfOpt = document.createElement('option');
    activeProfOpt.value = '';
    activeProfOpt.textContent = '📁 ' + (translations['activeProfileFolder'] || 'Cartella Profilo Attivo');
    select.appendChild(activeProfOpt);

    systemDrives.forEach(d => {
        const opt = document.createElement('option');
        opt.value = d.Name;
        opt.textContent = `💾 ${d.Name} (${d.VolumeLabel || 'Disco'}) - ${d.FormattedFree} liberi su ${d.FormattedTotal}`;
        select.appendChild(opt);
    });

    if (selectedStorageTarget) {
        select.value = selectedStorageTarget;
    }

    // 2. Render Cards
    container.innerHTML = '';
    systemDrives.forEach(d => {
        const card = document.createElement('div');
        const isSelected = selectedStorageTarget === d.Name;
        card.className = 'drive-card' + (isSelected ? ' selected' : '');
        card.onclick = () => selectStorageTarget(d.Name);

        const pct = Math.min(100, Math.max(0, d.UsedPercentage || 0));
        const barClass = pct > 90 ? ' danger' : (pct > 75 ? ' warn' : '');
        const iconClass = d.DriveType === 'Removable' ? 'ph-device-mobile' : 'ph-hard-drive';

        card.innerHTML = `
            <div class="drive-card-header">
                <div class="drive-card-title">
                    <i class="ph ${iconClass}"></i>
                    <span>${d.Name} <small style="font-weight: normal; opacity: 0.8;">(${d.VolumeLabel || 'Disco'})</small></span>
                </div>
                <span class="drive-type-badge">${d.DriveFormat || d.DriveType}</span>
            </div>
            <div class="drive-bar-bg">
                <div class="drive-bar-fill${barClass}" style="width: ${pct}%;"></div>
            </div>
            <div class="drive-meta">
                <span>${pct}% usato (${d.FormattedUsed})</span>
                <span class="drive-free">${d.FormattedFree} liberi</span>
            </div>
        `;
        container.appendChild(card);
    });
}

function selectStorageTarget(targetPath) {
    selectedStorageTarget = targetPath;
    const select = document.getElementById('storageTargetSelect');
    if (select) select.value = targetPath;

    document.querySelectorAll('#storageDrivesContainer .drive-card').forEach(card => {
        const titleText = card.querySelector('.drive-card-title span')?.textContent || '';
        if (targetPath && titleText.startsWith(targetPath)) {
            card.classList.add('selected');
        } else {
            card.classList.remove('selected');
        }
    });

    analyzeStorage();
}

function onStorageTargetChanged() {
    const val = document.getElementById('storageTargetSelect')?.value || '';
    selectedStorageTarget = val;
    document.querySelectorAll('#storageDrivesContainer .drive-card').forEach(card => {
        const titleText = card.querySelector('.drive-card-title span')?.textContent || '';
        if (val && titleText.startsWith(val)) {
            card.classList.add('selected');
        } else {
            card.classList.remove('selected');
        }
    });
}

function renderCleanDrives() {
    const container = document.getElementById('cleanDrivesContainer');
    const select = document.getElementById('cleanTargetSelect');
    if (!container || !select) return;

    // 1. Populate Dropdown
    select.innerHTML = '';
    const tempOpt = document.createElement('option');
    tempOpt.value = 'SYSTEM_TEMP';
    tempOpt.textContent = '🧹 ' + (translations['systemTemp'] || 'File Temporanei di Sistema & Utente (%TEMP%)');
    select.appendChild(tempOpt);

    systemDrives.forEach(d => {
        const opt = document.createElement('option');
        opt.value = d.Name;
        opt.textContent = `💾 Unità ${d.Name} (${d.VolumeLabel || 'Disco'}) - ${d.FormattedFree} liberi`;
        select.appendChild(opt);
    });

    const activeProfOpt = document.createElement('option');
    activeProfOpt.value = 'ACTIVE_PROFILE';
    activeProfOpt.textContent = '📁 ' + (translations['activeProfileFolder'] || 'Cartelle del Profilo Attivo');
    select.appendChild(activeProfOpt);

    if (selectedCleanTarget) {
        select.value = selectedCleanTarget;
    }

    // 2. Render Cards
    container.innerHTML = '';

    // Card 1: System Temp
    const tempCard = document.createElement('div');
    const isTempSelected = selectedCleanTarget === 'SYSTEM_TEMP';
    tempCard.className = 'drive-card' + (isTempSelected ? ' selected' : '');
    tempCard.onclick = () => selectCleanTarget('SYSTEM_TEMP');
    tempCard.innerHTML = `
        <div class="drive-card-header">
            <div class="drive-card-title">
                <i class="ph ph-broom"></i>
                <span>File Temp di Sistema</span>
            </div>
            <span class="drive-type-badge">Windows / User</span>
        </div>
        <div style="font-size: 0.78rem; color: var(--text-secondary); line-height: 1.3;">
            %TEMP%, Crash Dumps, cache applicative e file temporanei di sistema.
        </div>
        <div class="drive-meta" style="margin-top: auto;">
            <span class="drive-free">Consigliato per liberare spazio</span>
        </div>
    `;
    container.appendChild(tempCard);

    // Cards for each drive
    systemDrives.forEach(d => {
        const card = document.createElement('div');
        const isSelected = selectedCleanTarget === d.Name;
        card.className = 'drive-card' + (isSelected ? ' selected' : '');
        card.onclick = () => selectCleanTarget(d.Name);

        const pct = Math.min(100, Math.max(0, d.UsedPercentage || 0));
        const barClass = pct > 90 ? ' danger' : (pct > 75 ? ' warn' : '');
        const iconClass = d.DriveType === 'Removable' ? 'ph-device-mobile' : 'ph-hard-drive';

        card.innerHTML = `
            <div class="drive-card-header">
                <div class="drive-card-title">
                    <i class="ph ${iconClass}"></i>
                    <span>Unità ${d.Name} <small style="font-weight: normal; opacity: 0.8;">(${d.VolumeLabel || 'Disco'})</small></span>
                </div>
                <span class="drive-type-badge">${d.DriveFormat || 'NTFS'}</span>
            </div>
            <div class="drive-bar-bg">
                <div class="drive-bar-fill${barClass}" style="width: ${pct}%;"></div>
            </div>
            <div class="drive-meta">
                <span>${pct}% usato</span>
                <span class="drive-free">${d.FormattedFree} liberi</span>
            </div>
        `;
        container.appendChild(card);
    });
}

function selectCleanTarget(targetPath) {
    selectedCleanTarget = targetPath;
    const select = document.getElementById('cleanTargetSelect');
    if (select) select.value = targetPath;

    document.querySelectorAll('#cleanDrivesContainer .drive-card').forEach((card, idx) => {
        if (targetPath === 'SYSTEM_TEMP') {
            if (idx === 0) card.classList.add('selected');
            else card.classList.remove('selected');
        } else {
            const titleText = card.querySelector('.drive-card-title span')?.textContent || '';
            if (titleText.includes(targetPath)) {
                card.classList.add('selected');
            } else {
                card.classList.remove('selected');
            }
        }
    });
}

function onCleanTargetChanged() {
    const val = document.getElementById('cleanTargetSelect')?.value || 'SYSTEM_TEMP';
    selectCleanTarget(val);
}

// --- Storage Analyzer View ---
let _storageProgressTimer = null;

function _startStorageProgress() {
    const container = document.getElementById('storageReport');
    if (!container) return;
    container.innerHTML = `
        <div class="storage-progress-wrap">
            <i class="ph ph-magnifying-glass scan-icon"></i>
            <div class="storage-progress-pct" id="storagePct">0%</div>
            <div class="storage-progress-bar-bg">
                <div class="storage-progress-bar-fill" id="storageBarFill"></div>
            </div>
            <div class="storage-progress-label">Scansione file in corso...</div>
        </div>`;
    let pct = 0;
    if (_storageProgressTimer) clearInterval(_storageProgressTimer);
    // Animate 0 -> 90% over ~8 s using easing (steps every 200 ms)
    _storageProgressTimer = setInterval(() => {
        if (pct < 90) {
            pct += (90 - pct) * 0.035;
            _setStorageProgressPct(Math.floor(pct));
        }
    }, 200);
}

function _setStorageProgressPct(pct) {
    const bar   = document.getElementById('storageBarFill');
    const label = document.getElementById('storagePct');
    if (bar)   bar.style.width   = pct + '%';
    if (label) label.textContent = pct + '%';
}

function _finishStorageProgress(callback) {
    if (_storageProgressTimer) {
        clearInterval(_storageProgressTimer);
        _storageProgressTimer = null;
    }
    _setStorageProgressPct(100);
    // Brief pause at 100% before rendering results
    setTimeout(() => { if (callback) callback(); }, 350);
}

function analyzeStorage() {
    _startStorageProgress();
    const target = selectedStorageTarget || document.getElementById('storageTargetSelect')?.value || '';
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage('analyzeStorage:' + target);
    }
}

function renderStorage(res) {
    if (!res) return;
    _finishStorageProgress(() => _doRenderStorage(res));
}

function _doRenderStorage(res) {
    const container = document.getElementById('storageReport');
    if (!container) return;
    container.innerHTML = '';

    const rootName = res.RootFolder ? ` su <b>${res.RootFolder}</b>` : '';
    const totalFormatted = res.FormattedTotalSize || ((res.TotalBytes / 1048576).toFixed(1) + ' MB');

    const summaryHeader = document.createElement('div');
    summaryHeader.className = 'glass';
    summaryHeader.style.padding = '12px 16px';
    summaryHeader.style.marginBottom = '16px';
    summaryHeader.innerHTML = `
        <div style="display:flex; justify-content:space-between; align-items:center; flex-wrap:wrap; gap:10px;">
            <div>
                <span style="font-size:1.1rem; font-weight:600; color:var(--neon-cyan);">Totale Spazio Scansionato: ${totalFormatted}</span>
                <div style="font-size:0.85rem; color:var(--text-secondary); margin-top:2px;">Analisi completata${rootName} (${(res.TotalFiles || 0).toLocaleString()} file indicizzati)</div>
            </div>
            <button class="btn-action" onclick="analyzeStorage()" style="padding:6px 12px; font-size:0.8rem;"><i class="ph ph-arrows-clockwise"></i> Rianalizza</button>
        </div>
    `;
    container.appendChild(summaryHeader);

    // Subdirectories bars
    if (res.HeaviestSubdirectories && res.HeaviestSubdirectories.length > 0) {
        const subSec = document.createElement('div');
        subSec.innerHTML = `<h4 style="margin: 15px 0 10px 0; color:var(--neon-cyan); display:flex; align-items:center; gap:6px;"><i class="ph ph-folder"></i> Cartelle più pesanti</h4>`;
        res.HeaviestSubdirectories.forEach(sub => {
            const barBox = document.createElement('div');
            barBox.className = 'glass';
            barBox.style.padding = '8px 12px';
            barBox.style.marginBottom = '8px';
            const sizeStr = (sub.TotalBytes >= 1073741824) 
                ? (sub.TotalBytes / 1073741824).toFixed(2) + ' GB' 
                : (sub.TotalBytes / 1048576).toFixed(1) + ' MB';

            barBox.innerHTML = `
                <div style="display:flex; justify-content:space-between; font-size:0.85rem; margin-bottom:4px;">
                    <span style="font-weight:600;">📁 ${sub.Name} <small style="color:var(--text-secondary); font-weight:normal;">(${(sub.FileCount || 0).toLocaleString()} file)</small></span>
                    <span style="color:var(--neon-cyan); font-weight:600;">${sizeStr} (${sub.PercentageOfTotal}%)</span>
                </div>
                <div class="drive-bar-bg">
                    <div style="background:var(--neon-purple); width:${sub.PercentageOfTotal}%; height:100%; border-radius:4px;"></div>
                </div>
            `;
            subSec.appendChild(barBox);
        });
        container.appendChild(subSec);
    }

    // Top Largest files
    if (res.TopLargestFiles && res.TopLargestFiles.length > 0) {
        const topSec = document.createElement('div');
        topSec.innerHTML = `<h4 style="margin: 20px 0 10px 0; color:var(--neon-cyan); display:flex; align-items:center; gap:6px;"><i class="ph ph-file-text"></i> I file più grandi</h4>`;
        const ul = document.createElement('ul');
        ul.style.listStyle = 'none';
        ul.style.padding = '0';
        ul.style.display = 'flex';
        ul.style.flexDirection = 'column';
        ul.style.gap = '6px';

        res.TopLargestFiles.slice(0, 15).forEach(f => {
            const li = document.createElement('li');
            li.className = 'glass';
            li.style.padding = '8px 12px';
            li.style.display = 'flex';
            li.style.justifyContent = 'space-between';
            li.style.alignItems = 'center';
            li.style.fontSize = '0.85rem';

            const leftDiv = document.createElement('div');
            leftDiv.style.flex = '1';
            leftDiv.style.overflow = 'hidden';
            leftDiv.style.textOverflow = 'ellipsis';
            leftDiv.style.whiteSpace = 'nowrap';
            leftDiv.style.marginRight = '12px';
            leftDiv.innerHTML = `<span style="font-weight:600;">${f.Name}</span><div style="font-size:0.75rem; color:var(--text-secondary); overflow:hidden; text-overflow:ellipsis;">${f.Path}</div>`;

            const rightDiv = document.createElement('div');
            rightDiv.style.display = 'flex';
            rightDiv.style.alignItems = 'center';
            rightDiv.style.gap = '10px';

            const sizeSpan = document.createElement('span');
            sizeSpan.style.color = 'var(--neon-cyan)';
            sizeSpan.style.fontWeight = '600';
            sizeSpan.textContent = f.FormattedSize;

            const btnReveal = document.createElement('button');
            btnReveal.className = 'btn-action';
            btnReveal.style.padding = '4px 8px';
            btnReveal.style.fontSize = '0.75rem';
            btnReveal.title = translations['revealInExplorer'] || 'Mostra in Esplora Risorse';
            btnReveal.innerHTML = '<i class="ph ph-folder-open"></i>';
            btnReveal.onclick = () => revealFile(f.Path);

            rightDiv.appendChild(sizeSpan);
            rightDiv.appendChild(btnReveal);

            li.appendChild(leftDiv);
            li.appendChild(rightDiv);
            ul.appendChild(li);
        });
        topSec.appendChild(ul);
        container.appendChild(topSec);
    }
}

// --- Cleanup View ---
function scanJunk() {
    currentCleanMode = 'junk';
    const container = document.getElementById('cleanupList');
    if (container) {
        container.innerHTML = '<div style="text-align:center; color:var(--neon-cyan); padding:30px;"><i class="ph ph-circle-notch ph-spin" style="font-size:2rem; margin-bottom:10px; display:block;"></i>Ricerca file temporanei e spazzatura in corso...</div>';
    }
    const target = selectedCleanTarget || document.getElementById('cleanTargetSelect')?.value || 'SYSTEM_TEMP';
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage('scanJunk:' + target);
    }
}

function scanInstallers() {
    currentCleanMode = 'installers';
    const container = document.getElementById('cleanupList');
    if (container) {
        container.innerHTML = '<div style="text-align:center; color:var(--neon-cyan); padding:30px;"><i class="ph ph-circle-notch ph-spin" style="font-size:2rem; margin-bottom:10px; display:block;"></i>Ricerca pacchetti installer in corso...</div>';
    }
    const target = selectedCleanTarget || document.getElementById('cleanTargetSelect')?.value || '';
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage('cleanInstallers:' + target);
    }
}

function executeCleanSelected() {
    if (currentCleanMode === 'installers') {
        recycleInstallers();
    } else {
        recycleJunk();
    }
}

function renderJunk(items) {
    junkData = items || [];
    currentCleanMode = 'junk';
    const container = document.getElementById('cleanupList');
    if (!container) return;
    container.innerHTML = '';

    const btnCleanToolbar = document.getElementById('btnCleanAction');
    const lblCleanToolbar = document.getElementById('lblCleanAction');

    if (junkData.length === 0) {
        container.innerHTML = '<div style="text-align:center; color:var(--text-secondary); padding:30px;"><i class="ph ph-check-circle" style="font-size:2.5rem; color:var(--neon-cyan); display:block; margin-bottom:8px;"></i>Nessun file temporaneo o vuoto rilevato nella posizione selezionata! Il sistema è pulito.</div>';
        if (lblCleanToolbar) lblCleanToolbar.textContent = translations['deleteSelected'] || 'Cancella Selezionati';
        return;
    }

    if (lblCleanToolbar) lblCleanToolbar.textContent = `Cancella Selezionati (${junkData.length})`;

    // Calculate total bytes
    let totalBytes = 0;
    junkData.forEach(item => { totalBytes += (item.Bytes || 0); });
    const formattedBytes = formatByteSize(totalBytes);

    const header = document.createElement('div');
    header.className = 'glass';
    header.style.padding = '12px 16px';
    header.style.marginBottom = '12px';
    header.style.display = 'flex';
    header.style.justifyContent = 'space-between';
    header.style.alignItems = 'center';
    header.style.flexWrap = 'wrap';
    header.style.gap = '10px';

    header.innerHTML = `
        <div style="display:flex; align-items:center; gap:12px;">
            <label style="display:flex; align-items:center; gap:6px; font-weight:600; cursor:pointer;">
                <input type="checkbox" id="chkSelectAllJunk" checked onchange="toggleSelectAllJunk(this.checked)">
                <span>Seleziona tutti</span>
            </label>
            <span style="font-size:0.9rem;">Trovati <b style="color:var(--neon-cyan);">${junkData.length}</b> file temporanei (<b style="color:#ff6b6b;">${formattedBytes}</b> recuperabili)</span>
        </div>
        <button class="btn-undo" onclick="recycleJunk()" style="font-weight:600;">
            <i class="ph ph-trash"></i> <span>Elimina File Selezionati</span>
        </button>
    `;
    container.appendChild(header);

    const listDiv = document.createElement('div');
    listDiv.style.display = 'flex';
    listDiv.style.flexDirection = 'column';
    listDiv.style.gap = '6px';

    junkData.forEach((item, index) => {
        const row = document.createElement('div');
        row.className = 'glass';
        row.style.padding = '10px 14px';
        row.style.display = 'flex';
        row.style.alignItems = 'center';
        row.style.gap = '12px';

        const chk = document.createElement('input');
        chk.type = 'checkbox';
        chk.checked = item.SelectedForRecycle !== false;
        chk.onchange = () => {
            item.SelectedForRecycle = chk.checked;
            updateJunkHeaderTotal();
        };

        const infoDiv = document.createElement('div');
        infoDiv.style.flex = '1';
        infoDiv.style.overflow = 'hidden';

        const titleRow = document.createElement('div');
        titleRow.style.display = 'flex';
        titleRow.style.alignItems = 'center';
        titleRow.style.gap = '8px';
        titleRow.style.fontSize = '0.88rem';

        const fileName = item.FileName || (item.Path ? item.Path.split(/[\\/]/).pop() : 'File');
        const reasonBadge = `<span style="font-size:0.7rem; padding:2px 6px; border-radius:4px; background:rgba(255,100,100,0.15); color:#ff6b6b; font-weight:600;">${item.Reason}</span>`;

        titleRow.innerHTML = `<span style="font-weight:600;">${fileName}</span> ${reasonBadge}`;

        const pathDiv = document.createElement('div');
        pathDiv.style.fontSize = '0.75rem';
        pathDiv.style.color = 'var(--text-secondary)';
        pathDiv.style.overflow = 'hidden';
        pathDiv.style.textOverflow = 'ellipsis';
        pathDiv.style.whiteSpace = 'nowrap';
        pathDiv.textContent = item.Path;

        infoDiv.appendChild(titleRow);
        infoDiv.appendChild(pathDiv);

        const sizeSpan = document.createElement('span');
        sizeSpan.style.fontSize = '0.85rem';
        sizeSpan.style.color = 'var(--neon-cyan)';
        sizeSpan.style.fontWeight = '600';
        sizeSpan.style.whiteSpace = 'nowrap';
        sizeSpan.textContent = item.FormattedSize || formatByteSize(item.Bytes || 0);

        row.appendChild(chk);
        row.appendChild(infoDiv);
        row.appendChild(sizeSpan);
        listDiv.appendChild(row);
    });

    container.appendChild(listDiv);
}

function toggleSelectAllJunk(checked) {
    junkData.forEach(item => { item.SelectedForRecycle = checked; });
    const container = document.getElementById('cleanupList');
    if (container) {
        container.querySelectorAll('input[type="checkbox"]').forEach(chk => {
            chk.checked = checked;
        });
    }
    updateJunkHeaderTotal();
}

function updateJunkHeaderTotal() {
    const selected = junkData.filter(i => i.SelectedForRecycle);
    const lblCleanToolbar = document.getElementById('lblCleanAction');
    if (lblCleanToolbar) {
        lblCleanToolbar.textContent = `Cancella Selezionati (${selected.length})`;
    }
}

function recycleJunk() {
    const selected = junkData.filter(i => i.SelectedForRecycle);
    if (selected.length === 0) {
        alert("Nessun file selezionato per l'eliminazione.");
        return;
    }

    let totalBytes = 0;
    selected.forEach(i => { totalBytes += (i.Bytes || 0); });
    const formattedBytes = formatByteSize(totalBytes);

    if (confirm(`Eliminare i ${selected.length} file temporanei selezionati liberando circa ${formattedBytes}?\n\nI file verranno rimossi in sicurezza.`)) {
        const container = document.getElementById('cleanupList');
        if (container) {
            container.innerHTML = '<div style="text-align:center; color:var(--neon-cyan); padding:30px;"><i class="ph ph-circle-notch ph-spin" style="font-size:2rem; margin-bottom:10px; display:block;"></i>Eliminazione file in corso...</div>';
        }
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.postMessage('recycleJunk:' + JSON.stringify(selected));
        }
    }
}

function onJunkCleaned(res) {
    const container = document.getElementById('cleanupList');
    if (!container) return;
    const count = res.Count || res.count || 0;
    const freed = res.FormattedBytes || res.formattedBytes || '0 B';

    container.innerHTML = `
        <div class="clean-success-banner">
            <i class="ph ph-check-circle"></i>
            <div>
                <b style="font-size:1rem;">Pulizia completata con successo!</b>
                <div style="margin-top:2px; font-size:0.85rem;">Eliminati <b>${count}</b> file temporanei, liberando <b>${freed}</b> di spazio su disco.</div>
            </div>
        </div>
        <div style="text-align:center; padding:20px;">
            <button class="btn-action" onclick="scanJunk()"><i class="ph ph-arrows-clockwise"></i> Esegui un'altra scansione</button>
        </div>
    `;

    junkData = [];
    const lblCleanToolbar = document.getElementById('lblCleanAction');
    if (lblCleanToolbar) lblCleanToolbar.textContent = translations['deleteSelected'] || 'Cancella Selezionati';
    loadDrives();
}

function renderInstallers(items) {
    installersData = items || [];
    currentCleanMode = 'installers';
    const container = document.getElementById('cleanupList');
    if (!container) return;
    container.innerHTML = '';

    const lblCleanToolbar = document.getElementById('lblCleanAction');

    if (installersData.length === 0) {
        container.innerHTML = '<div style="text-align:center; color:var(--text-secondary); padding:30px;"><i class="ph ph-check-circle" style="font-size:2.5rem; color:var(--neon-cyan); display:block; margin-bottom:8px;"></i>Nessun installer vecchio o duplicato rilevato.</div>';
        if (lblCleanToolbar) lblCleanToolbar.textContent = translations['deleteSelected'] || 'Cancella Selezionati';
        return;
    }

    if (lblCleanToolbar) lblCleanToolbar.textContent = `Cancella Installer (${installersData.length})`;

    let totalBytes = 0;
    installersData.forEach(item => { if (item.SelectedForRecycle) totalBytes += (item.Bytes || 0); });

    const header = document.createElement('div');
    header.className = 'glass';
    header.style.padding = '12px 16px';
    header.style.marginBottom = '12px';
    header.style.display = 'flex';
    header.style.justifyContent = 'space-between';
    header.style.alignItems = 'center';
    header.style.flexWrap = 'wrap';
    header.style.gap = '10px';

    header.innerHTML = `
        <div style="display:flex; align-items:center; gap:12px;">
            <label style="display:flex; align-items:center; gap:6px; font-weight:600; cursor:pointer;">
                <input type="checkbox" id="chkSelectAllInstallers" checked onchange="toggleSelectAllInstallers(this.checked)">
                <span>Seleziona tutti</span>
            </label>
            <span style="font-size:0.9rem;">Rilevati <b style="color:var(--neon-cyan);">${installersData.length}</b> installer (<b style="color:#ff6b6b;">${formatByteSize(totalBytes)}</b> selezionati)</span>
        </div>
        <button class="btn-undo" onclick="recycleInstallers()" style="font-weight:600;">
            <i class="ph ph-trash"></i> <span>Cestina Installer Selezionati</span>
        </button>
    `;
    container.appendChild(header);

    const listDiv = document.createElement('div');
    listDiv.style.display = 'flex';
    listDiv.style.flexDirection = 'column';
    listDiv.style.gap = '6px';

    installersData.forEach(item => {
        const row = document.createElement('div');
        row.className = 'glass';
        row.style.padding = '10px 14px';
        row.style.display = 'flex';
        row.style.alignItems = 'center';
        row.style.gap = '12px';

        const chk = document.createElement('input');
        chk.type = 'checkbox';
        chk.checked = item.SelectedForRecycle;
        chk.onchange = () => { item.SelectedForRecycle = chk.checked; };

        const tag = item.IsOlderVersion ? '<span style="color:#ff6464; font-size:0.75rem; font-weight:600; background:rgba(255,100,100,0.15); padding:2px 6px; border-radius:4px;">[Versione Vecchia]</span>' : '';
        const span = document.createElement('div');
        span.style.flex = '1';
        span.style.fontSize = '0.85rem';
        span.innerHTML = `<span style="font-weight:600;">${item.FileName}</span> ${tag} <div style="color:var(--text-secondary); font-size:0.75rem;">${item.Path} (${new Date(item.LastModified).toLocaleDateString()})</div>`;

        const sizeSpan = document.createElement('span');
        sizeSpan.style.color = 'var(--neon-cyan)';
        sizeSpan.style.fontWeight = '600';
        sizeSpan.style.fontSize = '0.85rem';
        sizeSpan.textContent = formatByteSize(item.Bytes || 0);

        row.appendChild(chk);
        row.appendChild(span);
        row.appendChild(sizeSpan);
        listDiv.appendChild(row);
    });
    container.appendChild(listDiv);
}

function toggleSelectAllInstallers(checked) {
    installersData.forEach(item => { item.SelectedForRecycle = checked; });
    const container = document.getElementById('cleanupList');
    if (container) {
        container.querySelectorAll('input[type="checkbox"]').forEach(chk => { chk.checked = checked; });
    }
}

function recycleInstallers() {
    const selected = installersData.filter(i => i.SelectedForRecycle);
    if (selected.length === 0) {
        alert("Nessun installer selezionato.");
        return;
    }
    if (confirm(`Spostare i ${selected.length} pacchetti installer selezionati nel Cestino?`)) {
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.postMessage('recycleInstallers:' + JSON.stringify(selected));
        }
    }
}

function onInstallersCleaned(res) {
    const container = document.getElementById('cleanupList');
    if (!container) return;
    const count = res.Count || res.count || 0;
    const freed = res.FormattedBytes || res.formattedBytes || '0 B';

    container.innerHTML = `
        <div class="clean-success-banner">
            <i class="ph ph-check-circle"></i>
            <div>
                <b style="font-size:1rem;">Pulizia installer completata!</b>
                <div style="margin-top:2px; font-size:0.85rem;">Spostati nel Cestino <b>${count}</b> pacchetti installer, liberando <b>${freed}</b>.</div>
            </div>
        </div>
        <div style="text-align:center; padding:20px;">
            <button class="btn-action" onclick="scanInstallers()"><i class="ph ph-arrows-clockwise"></i> Scansiona di nuovo</button>
        </div>
    `;

    installersData = [];
    const lblCleanToolbar = document.getElementById('lblCleanAction');
    if (lblCleanToolbar) lblCleanToolbar.textContent = translations['deleteSelected'] || 'Cancella Selezionati';
    loadDrives();
}

function formatByteSize(bytes) {
    if (!bytes || bytes <= 0) return '0 B';
    if (bytes >= 1099511627776) return (bytes / 1099511627776).toFixed(2) + ' TB';
    if (bytes >= 1073741824) return (bytes / 1073741824).toFixed(2) + ' GB';
    if (bytes >= 1048576) return (bytes / 1048576).toFixed(1) + ' MB';
    if (bytes >= 1024) return (bytes / 1024).toFixed(0) + ' KB';
    return bytes + ' B';
}

function revealFile(filePath) {
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage('revealFile:' + filePath);
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
        case 'drivesData':
            renderDrives(payload);
            break;
        case 'storageData':
            renderStorage(payload);
            break;
        case 'junkData':
            renderJunk(payload);
            break;
        case 'junkCleaned':
            onJunkCleaned(payload);
            break;
        case 'installerData':
            renderInstallers(payload);
            break;
        case 'installersCleaned':
            onInstallersCleaned(payload);
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
