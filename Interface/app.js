// Notify backend when DOM is ready
document.addEventListener('DOMContentLoaded', () => {
    console.log('DOM fully loaded');
    if(window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage('pageReady');
    }
});

// Navigation Logic
function showView(viewId) {
    // Update Sidebar Active State
    document.querySelectorAll('.nav-item').forEach(nav => nav.classList.remove('active'));
    
    // Find item based on viewId
    const navItems = document.querySelectorAll('.nav-item');
    const views = ['dashboard', 'settings', 'logs'];
    const titles = ['Dashboard', 'Impostazioni', 'Registro Attività'];
    
    const index = views.indexOf(viewId);
    if (index !== -1) {
        navItems[index].classList.add('active');
        
        // Switch View
        document.querySelectorAll('.view-section').forEach(el => el.classList.remove('active'));
        const targetView = document.getElementById('view-' + viewId);
        if(targetView) targetView.classList.add('active');
        
        // Update Breadcrumb
        const pageTitle = document.getElementById('pageTitle');
        if(pageTitle) pageTitle.innerText = 'Sistema / ' + titles[index];
    }
}

// Add Drag & Drop visual feedback
const dropZone = document.getElementById('dropZone');
if (dropZone) {
    dropZone.addEventListener('dragover', (e) => { e.preventDefault(); dropZone.style.borderColor = 'var(--neon-green)'; });
    dropZone.addEventListener('dragleave', (e) => { e.preventDefault(); dropZone.style.borderColor = ''; });
    dropZone.addEventListener('drop', (e) => { e.preventDefault(); dropZone.style.borderColor = ''; });
}

// --- Functions called by C# ---

function updateProgress(percent, label) {
    const effVal = document.getElementById('efficiencyVal');
    if(effVal) effVal.innerHTML = percent + '<span class="percent">%</span>';
    
    if (label) {
        const heroLabel = document.querySelector('.hero-label');
        if (heroLabel) heroLabel.innerText = label;
    }
}

// Ensure updateStats doesn't overwrite active progress if called
function updateStats(files, space, isMonitoring, deletedCount) {
    const elFiles = document.getElementById('statFiles');
    if(elFiles) elFiles.innerText = files;
    
    const elDeleted = document.getElementById('statDeleted');
    if(elDeleted) elDeleted.innerText = deletedCount || '0';
    
    const elSpace = document.getElementById('statSpace');
    if(elSpace) elSpace.innerText = space;
    
    const monEl = document.getElementById('statMonitor');
    const iconEl = document.getElementById('iconMonitor');
    
    if(monEl) {
        monEl.innerText = isMonitoring ? 'ON' : 'OFF';
        monEl.className = isMonitoring ? 'stat-value text-green pulse-text' : 'stat-value text-secondary';
    }
    
    if(iconEl) {
        iconEl.innerHTML = isMonitoring ? '<i class="ph ph-wifi-high"></i>' : '<i class="ph ph-wifi-slash"></i>';
        iconEl.className = isMonitoring ? 'stat-icon green-bg' : 'stat-icon purple-bg';
    }

    // Update Ring ONLY if not currently in progress (this is a simple heuristic)
    const heroLabel = document.querySelector('.hero-label');
    if (heroLabel && heroLabel.innerText !== 'Elaborazione...') {
        const effVal = document.getElementById('efficiencyVal');
        if(effVal) effVal.innerHTML = isMonitoring ? '100<span class="percent">%</span>' : '0<span class="percent">%</span>';
    }
}

function showDetails(title, filesJson) {
    const modal = document.getElementById('modal-container');
    const titleEl = document.getElementById('modal-title');
    const listEl = document.getElementById('details-list');
    
    if(!modal || !titleEl || !listEl) return;
    
    titleEl.innerText = title;
    listEl.innerHTML = '';
    
    const files = JSON.parse(filesJson);
    if(files.length === 0) {
        listEl.innerHTML = '<li>Nessun file presente per questa categoria.</li>';
    } else {
        files.forEach(f => {
            const li = document.createElement('li');
            // Check if f is object or string
            if (typeof f === 'object') {
                li.innerHTML = `<b>${f.Name}</b><span>Da: ${f.Old}</span><span>A: ${f.New}</span>`;
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
    if(list) {
        const li = document.createElement('li');
        li.innerText = new Date().toLocaleTimeString() + " - " + message;
        list.prepend(li); // Newest first
        if (list.children.length > 50) list.lastChild.remove();
    }
}

// Rules Logic Removed

function toggleSetting(name, value) {
    if(window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage('set:' + name + ':' + value);
    }
}
