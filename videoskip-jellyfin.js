// VideoSkip for Jellyfin
// Inject via the "JavaScript Injector" Jellyfin plugin
// Paste this entire file into the plugin's script box

// ─── Configuration ────────────────────────────────────────────────────────────

const SKP_PLUGIN_ENDPOINT = '/api/videoskip/';

// ─── State ────────────────────────────────────────────────────────────────────

let cuts = [];
let videoEl = null;
let blurBoxEl = null;
let inCut = false;
let lastItemId = null;
let lastStatus = { text: 'No file loaded', color: '#aaa' };

// ─── Bootstrap ────────────────────────────────────────────────────────────────

const observer = new MutationObserver(() => {
  const osd = document.querySelector('.videoOsdBottom');
  if (osd && !document.getElementById('vs-panel')) {
    injectPanel(osd);
    injectBlurBox();
  }

  const newVideo = document.querySelector('video');
  if (newVideo && newVideo !== videoEl) {
    videoEl = newVideo;
    inCut = false;
    cuts = [];
    attachVideoHook();
    tryAutoLoad();
  }

  // If video element is gone, reset so re-entering triggers a fresh load
  if (!newVideo && videoEl) {
    videoEl = null;
    lastItemId = null;
    cuts = [];
    inCut = false;
    setStatus('No file loaded', '#aaa');
  }
});

observer.observe(document.body, { childList: true, subtree: true });

// ─── Panel UI ─────────────────────────────────────────────────────────────────

function injectPanel(osd) {
  const panel = document.createElement('div');
  panel.id = 'vs-panel';
  panel.style.cssText = `
    position: fixed;
    bottom: 800px;
    right: 20px;
    background: rgba(0, 0, 0, 0.75);
    padding: 12px 16px;
    border-radius: 8px;
    color: white;
    font-size: 13px;
    font-family: sans-serif;
    z-index: 1000;
    min-width: 250px;
    user-select: none;
    pointer-events: auto;
  `;

  panel.innerHTML = `
    <div id="vs-header" style="display:flex; justify-content:space-between; align-items:center; margin-bottom:10px; cursor:pointer;">
      <strong>VideoSkip</strong>
      <div style="display:flex; align-items:center; gap:8px;">
        <span id="vs-status" style="font-size:11px; color:#aaa;">No file loaded</span>
        <span id="vs-toggle" style="font-size:11px; color:#aaa;">▲</span>
      </div>
    </div>

    <div id="vs-body" style="display:none;">
      <input type="file" id="vs-file-input" accept=".skp" style="display:none"/>
      <button id="vs-load-btn" style="
        width:100%; padding:6px; margin-bottom:6px;
        background:#444; border:none; border-radius:4px;
        color:white; cursor:pointer; font-size:13px;
      ">Load .skp file</button>

      <button id="vs-cuts-btn" style="
        width:100%; padding:6px; margin-bottom:12px;
        background:#444; border:none; border-radius:4px;
        color:white; cursor:pointer; font-size:13px;
      ">View Cuts</button>

      <div id="vs-cuts-list" style="
        display:none;
        max-height:200px;
        overflow-y:auto;
        margin-bottom:12px;
        font-size:11px;
        color:#ccc;
      "></div>

      <div style="margin-bottom:8px; font-weight:bold; color:#ccc;">Filter levels (0 = off, 3 = strict)</div>

      ${makeSlider('sex',       'Sex')}
      ${makeSlider('violence',  'Violence')}
      ${makeSlider('profanity', 'Profanity')}
      ${makeSlider('other',     'Other')}
    </div>
  `;

  osd.appendChild(panel);

  // Restore status in case auto-load already ran before panel was injected
  const statusEl = document.getElementById('vs-status');
  if (statusEl) {
    statusEl.textContent = lastStatus.text;
    statusEl.style.color = lastStatus.color;
  }

  // Stop clicks from reaching the video player behind the panel
  ['click', 'mousedown', 'pointerdown'].forEach(evt => {
    panel.addEventListener(evt, e => e.stopPropagation());
  });

  wireListeners();
  makeDraggable(panel);
}

function makeDraggable(panel) {
  const header = document.getElementById('vs-header');
  if (!header) return;

  let isDragging = false;
  let didDrag = false;
  let startX, startY, startLeft, startTop;

  header.addEventListener('mousedown', (e) => {
    if (e.target.id === 'vs-toggle') return;
    isDragging = true;
    didDrag = false;

    const rect = panel.getBoundingClientRect();
    startX    = e.clientX;
    startY    = e.clientY;
    startLeft = rect.left;
    startTop  = rect.top;

    panel.style.right  = 'auto';
    panel.style.bottom = 'auto';
    panel.style.left   = startLeft + 'px';
    panel.style.top    = startTop  + 'px';

    e.preventDefault();
    e.stopPropagation();
  });

  document.addEventListener('mousemove', (e) => {
    if (!isDragging) return;
    const dx = e.clientX - startX;
    const dy = e.clientY - startY;
    if (Math.abs(dx) > 3 || Math.abs(dy) > 3) didDrag = true;
    panel.style.left = (startLeft + dx) + 'px';
    panel.style.top  = (startTop  + dy) + 'px';
  });

  document.addEventListener('mouseup', () => {
    isDragging = false;
  });

  // Suppress the click that fires after a drag
  header.addEventListener('click', (e) => {
    if (didDrag) {
      didDrag = false;
      e.stopImmediatePropagation();
    }
  }, true);
}

function makeSlider(id, label) {
  return `
    <div style="display:flex; align-items:center; gap:8px; margin-bottom:6px;">
      <span style="width:68px; color:#ddd;">${label}</span>
      <input type="range" id="vs-${id}" min="0" max="3" value="3" style="flex:1; cursor:pointer;"/>
      <span id="vs-${id}-val" style="width:12px; text-align:right; color:#aaa;">3</span>
    </div>
  `;
}

// ─── Blur Box ─────────────────────────────────────────────────────────────────

function injectBlurBox() {
  if (document.getElementById('vs-blur-box')) return;

  const box = document.createElement('div');
  box.id = 'vs-blur-box';
  box.style.cssText = `
    position: absolute;
    z-index: 5;
    display: none;
    border-radius: 500px;
    -webkit-backdrop-filter: blur(20px);
    backdrop-filter: blur(20px);
    pointer-events: none;
  `;

  const videoContainer = videoEl?.parentElement ?? document.body;
  videoContainer.style.position = 'relative';
  videoContainer.appendChild(box);
  blurBoxEl = box;
}

function showBlurBox(coords) {
  if (!blurBoxEl || !videoEl) return;

  const [x1, y1, x2, y2] = coords;
  const vw = videoEl.clientWidth;
  const vh = videoEl.clientHeight;
  const vLeft = videoEl.offsetLeft;
  const vTop  = videoEl.offsetTop;

  blurBoxEl.style.left   = (vLeft + (x1 / 100) * vw) + 'px';
  blurBoxEl.style.top    = (vTop  + (y1 / 100) * vh) + 'px';
  blurBoxEl.style.width  = ((x2 - x1) / 100 * vw) + 'px';
  blurBoxEl.style.height = ((y2 - y1) / 100 * vh) + 'px';
  blurBoxEl.style.display = 'block';
}

function hideBlurBox() {
  if (blurBoxEl) blurBoxEl.style.display = 'none';
}

// ─── Event Wiring ─────────────────────────────────────────────────────────────

function wireListeners() {
  document.getElementById('vs-header').addEventListener('click', () => {
    const body   = document.getElementById('vs-body');
    const toggle = document.getElementById('vs-toggle');
    const expanded = body.style.display !== 'none';
    body.style.display = expanded ? 'none' : 'block';
    toggle.textContent = expanded ? '▲' : '▼';
  });

  document.getElementById('vs-load-btn').addEventListener('click', () => {
    document.getElementById('vs-file-input').click();
  });
  document.getElementById('vs-file-input').addEventListener('change', handleFileLoad);
  document.getElementById('vs-cuts-btn').addEventListener('click', toggleCutsList);

  ['sex', 'violence', 'profanity', 'other'].forEach(cat => {
    const slider = document.getElementById(`vs-${cat}`);
    const label  = document.getElementById(`vs-${cat}-val`);
    slider.addEventListener('input', () => {
      label.textContent = slider.value;
    });
  });
}

// ─── Cuts List ────────────────────────────────────────────────────────────────

function toggleCutsList() {
  const list = document.getElementById('vs-cuts-list');
  const btn  = document.getElementById('vs-cuts-btn');
  if (!list) return;

  if (list.style.display === 'none') {
    if (cuts.length === 0) {
      list.innerHTML = '<div style="color:#888; padding:4px;">No cuts loaded</div>';
    } else {
      list.innerHTML = cuts.map((c, i) => `
        <div style="padding:4px 0; border-bottom:1px solid #333;">
          <span style="color:#aaa;">${i + 1}.</span>
          <span
            class="vs-jump"
            data-time="${c.start}"
            style="color:#6af; cursor:pointer; text-decoration:underline;"
          > ${toHMS(c.start)} → ${toHMS(c.end)}</span>
          <span style="color:#888; margin-left:6px;">${c.category} / ${c.action} / sev ${c.severity}</span>
        </div>
      `).join('');

      // Wire up jump clicks
      list.querySelectorAll('.vs-jump').forEach(el => {
        el.addEventListener('click', () => {
          if (videoEl) videoEl.currentTime = parseFloat(el.dataset.time);
        });
      });
    }
    list.style.display = 'block';
    btn.textContent = 'Hide Cuts';
  } else {
    list.style.display = 'none';
    btn.textContent = 'View Cuts';
  }
}

function toHMS(seconds) {
  const h = Math.floor(seconds / 3600);
  const m = Math.floor((seconds % 3600) / 60);
  const s = (seconds % 60).toFixed(3);
  return h > 0
    ? `${h}:${String(m).padStart(2, '0')}:${String(s).padStart(4, '0')}`
    : `${m}:${String(s).padStart(4, '0')}`;
}

// ─── Auto-load ────────────────────────────────────────────────────────────────

function waitForVideoSrc() {
  return new Promise((resolve) => {
    if (videoEl?.src) {
      resolve(videoEl.src);
      return;
    }

    let attempts = 0;
    const interval = setInterval(() => {
      attempts++;
      if (videoEl?.src) {
        clearInterval(interval);
        resolve(videoEl.src);
      } else if (attempts > 50) {
        clearInterval(interval);
        resolve(null);
      }
    }, 100);
  });
}

async function tryAutoLoad() {
  try {
    const videoSrc = await waitForVideoSrc();
    if (!videoSrc) return;

    const srcMatch = videoSrc.match(/\/Videos\/([a-f0-9]{32})/i);
    const itemId = srcMatch?.[1] ?? null;
    if (!itemId) return;

    if (itemId === lastItemId) return;
    lastItemId = itemId;

    cuts = [];
    restoreVideo();
    setStatus('Searching...', '#aaa');

    const apiClient = window.ApiClient;
    if (!apiClient) {
      console.log('[VideoSkip] ApiClient not available');
      return;
    }

    const token = apiClient.accessToken();

    const skpRes = await fetch(`${SKP_PLUGIN_ENDPOINT}${itemId}`, {
      headers: { 'X-Emby-Token': token }
    });

    if (!skpRes.ok) {
      console.log(`[VideoSkip] Plugin returned ${skpRes.status} for item ${itemId}`);
      setStatus('No .skp found', '#888');
      return;
    }

    const text = await skpRes.text();
    cuts = parseSkp(text.split('data:image')[0]);

    setStatus(`${cuts.length} cut${cuts.length !== 1 ? 's' : ''} (auto)`, cuts.length > 0 ? '#6f6' : '#f66');
    console.log(`[VideoSkip] Auto-loaded ${cuts.length} cuts for item ${itemId}`);

  } catch (err) {
    console.log('[VideoSkip] Auto-load error:', err);
    setStatus('No file loaded', '#aaa');
  }
}

function setStatus(text, color) {
  lastStatus = { text, color };
  const status = document.getElementById('vs-status');
  if (status) {
    status.textContent = text;
    status.style.color = color;
  }
}

// ─── File Parsing ─────────────────────────────────────────────────────────────

function handleFileLoad(e) {
  const file = e.target.files[0];
  if (!file) return;

  const reader = new FileReader();
  reader.onload = (ev) => {
    const text = ev.target.result.split('data:image')[0];
    cuts = parseSkp(text);
    setStatus(`${cuts.length} cut${cuts.length !== 1 ? 's' : ''} loaded`, cuts.length > 0 ? '#6f6' : '#f66');
  };
  reader.readAsText(file);
}

function parseSkp(text) {
  const results = [];
  const blocks = text.trim().split(/\n\s*\n/);

  for (const block of blocks) {
    const lines = block.trim().split('\n');
    if (lines.length < 2) continue;

    const tsIndex = lines.findIndex(l => l.includes('-->'));
    if (tsIndex === -1) continue;

    const [startStr, endStr] = lines[tsIndex].split('-->').map(s => s.trim());
    const start = fromHMS(startStr);
    const end   = fromHMS(endStr);
    if (isNaN(start) || isNaN(end)) continue;

    const meta = lines[tsIndex + 1]?.trim().toLowerCase() ?? '';

    const coordMatch = meta.match(/\[([^\]]+)\]/);
    const coords = coordMatch
      ? coordMatch[1].split(',').map(Number)
      : null;

    const cleanMeta = meta.replace(/\[.*\]/, '').trim();
    const parts = cleanMeta.split(/\s+/);

    const rawCategory = parts[0] ?? 'other';

    results.push({
      start,
      end,
      category: rawCategory === 'nudity' ? 'sex' : rawCategory,
      action:   parts[1] ?? 'skip',
      severity: parseInt(parts[2]) || 3,
      coords,
    });
  }

  return results;
}

function fromHMS(s) {
  const clean = s.replace(',', '.');
  const parts = clean.split(':');
  if (parts.length === 3) {
    return parseFloat(parts[0]) * 3600
         + parseFloat(parts[1]) * 60
         + parseFloat(parts[2]);
  }
  if (parts.length === 2) {
    return parseFloat(parts[0]) * 60 + parseFloat(parts[1]);
  }
  return NaN;
}

// ─── Skip Logic ───────────────────────────────────────────────────────────────

function shouldApply(cut) {
  const slider = document.getElementById(`vs-${cut.category}`);
  if (!slider) return false;

  const threshold = parseInt(slider.value);
  if (threshold === 0) return false;

  const minSeverity = 4 - threshold;
  return cut.severity >= minSeverity;
}

// ─── Video Hook ───────────────────────────────────────────────────────────────

function attachVideoHook() {
  if (!videoEl) return;
  videoEl.addEventListener('timeupdate', onTimeUpdate);
}

function onTimeUpdate() {
  if (!videoEl || cuts.length === 0) return;

  const t = videoEl.currentTime;
  const activeCuts = cuts.filter(c => t >= c.start && t < c.end && shouldApply(c));

  if (activeCuts.length > 0) {
    // Restore first so actions compose cleanly on each tick
    restoreVideo();
    activeCuts.forEach(applyAction);
    inCut = true;
  } else if (inCut) {
    restoreVideo();
    inCut = false;
  }
}

function applyAction(cut) {
  if (cut.coords) {
    switch (cut.action) {
      case 'blur':
      case 'blank':
        showBlurBox(cut.coords);
        break;
    }
    return;
  }

  switch (cut.action) {
    case 'skip':
      videoEl.currentTime = cut.end;
      break;
    case 'mute':
      videoEl.muted = true;
      break;
    case 'blank':
      videoEl.style.visibility = 'hidden';
      break;
    case 'blur':
      videoEl.style.filter = 'blur(24px)';
      break;
  }
}

function restoreVideo() {
  if (!videoEl) return;
  videoEl.muted = false;
  videoEl.style.visibility = 'visible';
  videoEl.style.filter = 'none';
  hideBlurBox();
}
