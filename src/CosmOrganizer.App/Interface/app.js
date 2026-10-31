// CosmOrganizer Interface Client Script
// © CosmoNet.info — MIT License

let currentLang = 'it';
let translations = {};
let previewData = [];

// Notify backend when DOM is ready
document.addEventListener('DOMContentLoaded', async () => {
    console.log('DOM fully loaded');
    await loadTranslations(currentLang);
    applyThemeFromStorage();

    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.addEventListener('message', event => {
            if (event.data) handleMessage(event.data);
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

// Navigation Logic
function showView(viewId) {
    document.querySelectorAll('.nav-item').forEach(nav => nav.classList.remove('active'));
    
    const views = ['dashboard', 'rules', 'preview', 'settings', 'logs'];
    const navItems = document.querySelectorAll('.nav-item');
    const index = views.indexOf(viewId);
    
    if (index !== -1 && navItems[index]) {
        navItems[index].classList.add('active');
        document.querySelectorAll('.view-section').forEach(el => el.style.display = 'none');
        
        const targetView = document.getElementById('view-' + viewId);
        if (targetView) targetView.style.display = 'block';
        
        const pageTitle = document.getElementById('pageTitle');
        if (pageTitle) {
            const titleKeys = {
                dashboard: 'systemDashboard',
                rules: 'systemRules',
                preview: 'systemPreview',
                settings: 'systemSettings',
                logs: 'systemLogs'
            };
            const key = titleKeys[viewId];
            pageTitle.textContent = translations[key] || ('System / ' + viewId);
        }
    }
}

// Add Drag & Drop visual feedback
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
        testBtn.textContent = '🧪 Prova';
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
        case 'log':
            addLog(payload);
            break;
        case 'toast':
            alert(payload);
            break;
    }
}

// --- Functions called by C# or legacy calls ---
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
}

function showUpdateBanner(v) {
    const banner = document.getElementById('updateBanner');
    const txt = document.getElementById('updateBannerText');
    if (banner && txt) {
        txt.textContent = 'Aggiornamento ' + v + ' pronto — Riavvia per installare';
        banner.style.display = 'flex';
    }
}
