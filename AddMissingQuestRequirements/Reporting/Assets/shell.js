(async function boot() {
  const toast = document.getElementById('mqw-toast');
  function showToast(msg, isError) {
    if (!toast) { return; }
    toast.textContent = msg;
    toast.className = isError ? 'toast error' : 'toast';
    toast.classList.remove('hidden');
  }
  function clearToast() {
    if (toast) { toast.classList.add('hidden'); }
  }

  async function refresh() {
    const resp = await fetch('/api/state');
    if (!resp.ok) { showToast('Failed to fetch state: HTTP ' + resp.status, true); return; }
    const data = await resp.json();
    persistSessionState();
    renderInspector(data, document.body);
    hydrateSessionState();
  }

  await refresh();

  if (typeof EventSource !== 'undefined') {
    const es = new EventSource('/api/events');
    es.addEventListener('state-changed', () => { clearToast(); refresh(); });
    es.addEventListener('reload-error', e => {
      try {
        const payload = JSON.parse(e.data);
        showToast('Config reload failed: ' + (payload.message || 'unknown'), true);
      } catch {
        showToast('Config reload failed', true);
      }
    });
    es.onerror = () => { /* EventSource auto-reconnects */ };
  }
})();
