// ── Module state ─────────────────────────────────────────────────────────────
let __DATA__ = null;

// ── Tab switching ────────────────────────────────────────────────────────────
function showTab(name) {
  document.querySelectorAll('.tab-btn').forEach(b => b.classList.remove('active'));
  document.querySelectorAll('.tab-content').forEach(t => t.classList.remove('active'));
  document.getElementById('tab-' + name).classList.add('active');
  document.querySelector(`.tab-btn[onclick="showTab('${name}')"]`).classList.add('active');
}

function esc(s) {
  return String(s ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/'/g,'&#39;');
}

// Mod-group arrays are conceptually a set-of-groups where each group is a
// set-of-ids (intra-group AND, cross-group OR). Diffing them by array index
// shows spurious -/+ lines whenever the pipeline emits groups in a different
// order. Key each group by its sorted id list and diff by key presence.
function renderModGroupDiffs(label, before, after) {
  if (before.length === 0 && after.length === 0) { return ''; }

  const keyOf = g => g.map(w => w.id).slice().sort().join(',');
  const sortItems = g => g.slice().sort((a, b) => a.id.localeCompare(b.id));

  const byKey = new Map();
  before.forEach(g => {
    const k = keyOf(g);
    if (!byKey.has(k)) { byKey.set(k, { key: k, before: g, after: null }); }
  });
  after.forEach(g => {
    const k = keyOf(g);
    const existing = byKey.get(k);
    if (existing) { existing.after = g; }
    else { byKey.set(k, { key: k, before: null, after: g }); }
  });

  const entries = [...byKey.values()].sort((a, b) => a.key.localeCompare(b.key));

  const itemLine = (marker, cls, w) =>
    `<div class="${cls}">${marker} ${esc(w.name)} <span class="id-hover" title="${esc(w.id)}">${esc(w.id)}</span></div>`;

  let html = '';
  entries.forEach((e, i) => {
    html += `<div class="mod-group-label">${label} [${i}]</div>`;
    html += '<div class="git-diff">';
    if (e.before && e.after) {
      sortItems(e.after).forEach(w => { html += itemLine('&nbsp;', 'diff-same', w); });
    } else if (e.before) {
      sortItems(e.before).forEach(w => { html += itemLine('-', 'diff-removed', w); });
    } else {
      sortItems(e.after).forEach(w => { html += itemLine('+', 'diff-added', w); });
    }
    html += '</div>';
  });
  return html;
}

function countGroupAdditions(before, after) {
  const keyOf = g => g.map(w => w.id).slice().sort().join(',');
  const beforeKeys = new Set(before.map(keyOf));
  let n = 0;
  after.forEach(g => {
    if (!beforeKeys.has(keyOf(g))) { n += g.length; }
  });
  return n;
}

// ── Settings ─────────────────────────────────────────────────────────────────
function renderSettings() {
  const s = __DATA__.settings;
  let html = `<h2>Global Settings</h2>
  <table style="width:auto">
    <tr><th>Setting</th><th>Value</th></tr>
    <tr><td>IncludeParentCategories</td><td>${s.includeParentCategories}</td></tr>
    <tr><td>BestCandidateExpansion</td><td>${s.bestCandidateExpansion}</td></tr>
    <tr><td>Excluded Quests</td><td>${s.excludedQuestCount}</td></tr>
  </table>`;

  html += `<h2>Type Rules <span class="count-badge">${(s.rules||[]).length}</span></h2>`;
  html += `<div class="filter-bar" style="margin-bottom:8px">
    <input type="search" id="rule-search" placeholder="Filter by type or condition..."
           oninput="filterRules()" style="width:300px">
  </div>`;
  html += `<div id="rules-list">`;
  (s.rules||[]).forEach((r, i) => {
    const tags = renderCondNodeTags(r.conditions);
    const alsoAs = (r.alsoAs||[]).length
      ? `<span class="rule-alsoAs">→ ${(r.alsoAs).map(a => `<span class="type-tag" style="cursor:default">${esc(a)}</span>`).join('')}</span>`
      : '';
    html += `<div class="rule-row" data-rule-idx="${i}"
               data-search="${esc(r.type)} ${esc(condNodeSearchText(r.conditions))}"
               onclick="toggleRuleDetail(${i})">
      <span class="rule-index mono">#${i+1}</span>
      <span class="rule-type">${esc(r.type)}</span>
      ${tags}
      ${alsoAs}
      ${r.priority ? `<span style="color:#555;font-size:10px">(${esc(r.priority)})</span>` : ''}
    </div>
    <div class="rule-detail" id="rule-detail-${i}">
      <div class="cond-tree">${renderCondNode(r.conditions)}</div>
    </div>`;
  });
  html += `</div>`;

  const mo = Object.entries(s.manualTypeOverrides || {});
  html += `<h2>Manual Type Overrides <span class="count-badge">${mo.length}</span></h2>`;
  if (mo.length) {
    html += `<table style="width:auto"><thead><tr><th>ID</th><th>Type</th></tr></thead><tbody>`;
    mo.forEach(([id, t]) => {
      html += `<tr><td class="mono">${esc(id)}</td><td>${esc(t)}</td></tr>`;
    });
    html += `</tbody></table>`;
  }

  html += `<h2>Attachment Type Rules <span class="count-badge">${(s.attachmentRules||[]).length}</span></h2>`;
  html += `<div class="filter-bar" style="margin-bottom:8px">
    <input type="search" id="attachment-rule-search" placeholder="Filter by type or condition..."
           oninput="filterAttachmentRules()" style="width:300px">
  </div>`;
  html += `<div id="attachment-rules-list">`;
  (s.attachmentRules||[]).forEach((r, i) => {
    const tags = renderCondNodeTags(r.conditions);
    const alsoAs = (r.alsoAs||[]).length
      ? `<span class="rule-alsoAs">→ ${(r.alsoAs).map(a => `<span class="type-tag" style="cursor:default">${esc(a)}</span>`).join('')}</span>`
      : '';
    html += `<div class="rule-row" data-rule-idx="${i}"
               data-search="${esc(r.type)} ${esc(condNodeSearchText(r.conditions))}"
               onclick="toggleAttachmentRuleDetail(${i})">
      <span class="rule-index mono">#${i+1}</span>
      <span class="rule-type">${esc(r.type)}</span>
      ${tags}
      ${alsoAs}
      ${r.priority ? `<span style="color:#555;font-size:10px">(${esc(r.priority)})</span>` : ''}
    </div>
    <div class="rule-detail" id="attachment-rule-detail-${i}">
      <div class="cond-tree">${renderCondNode(r.conditions)}</div>
    </div>`;
  });
  html += `</div>`;

  document.getElementById('settings-panel').innerHTML = html;
}

function renderCondNode(node) {
  if (!node) { return ''; }
  if (node.op) {
    const label = node.op === 'and' ? 'AND — all must match'
                : node.op === 'or'  ? 'OR — any must match'
                :                     'NOT — must not match';
    const cls = 'cond-' + node.op;
    const children = (node.children || []).map(renderCondNode).join('');
    return `<div class="${cls}"><div class="cond-op-label">${label}</div>${children}</div>`;
  }
  return `<div class="cond-leaf">
    <span class="cond-key">${esc(node.key)}</span>
    <span class="cond-val">${esc(node.value)}</span>
  </div>`;
}

function renderCondNodeTags(node) {
  if (!node) { return ''; }
  if (node.op) {
    return (node.children || []).map(renderCondNodeTags).join('');
  }
  return `<span class="type-tag" style="cursor:default">${esc(node.key)}: ${esc(node.value)}</span>`;
}

function condNodeSearchText(node) {
  if (!node) { return ''; }
  if (node.op) { return (node.children||[]).map(condNodeSearchText).join(' '); }
  return `${node.key} ${node.value}`;
}

function toggleRuleDetail(i) {
  const el = document.getElementById('rule-detail-' + i);
  el.classList.toggle('open');
}

function filterRules() {
  const q = document.getElementById('rule-search').value.toLowerCase();
  document.querySelectorAll('#rules-list .rule-row').forEach(row => {
    const detail = row.nextElementSibling;
    const match = !q || row.dataset.search.toLowerCase().includes(q);
    row.classList.toggle('hidden', !match);
    if (detail) { detail.classList.toggle('hidden', !match); }
  });
}

function toggleAttachmentRuleDetail(i) {
  const el = document.getElementById('attachment-rule-detail-' + i);
  el.classList.toggle('open');
}

function filterAttachmentRules() {
  const q = document.getElementById('attachment-rule-search').value.toLowerCase();
  document.querySelectorAll('#attachment-rules-list .rule-row').forEach(row => {
    const detail = row.nextElementSibling;
    const match = !q || row.dataset.search.toLowerCase().includes(q);
    row.classList.toggle('hidden', !match);
    if (detail) { detail.classList.toggle('hidden', !match); }
  });
}

// ── Weapons ──────────────────────────────────────────────────────────────────
function renderWeapons() {
  const tbody = document.getElementById('weapon-tbody');
  (__DATA__.weapons||[]).forEach(w => {
    const types = (w.types||[]).map(t =>
      `<span class="type-tag" onclick="jumpToType('${esc(t)}')">${esc(t)}</span>`
    ).join('');
    const tr = document.createElement('tr');
    tr.id = 'weapon-' + w.id;
    tr.dataset.search = `${w.name} ${w.id}`.toLowerCase();
    tr.innerHTML = `
      <td>
        ${esc(w.name)}<br>
        <span class="id-hover mono" title="${esc(w.id)}">${esc(w.id)}</span>
      </td>
      <td>${types}</td>
      <td class="mono">${esc(w.caliber||'')}</td>`;
    tbody.appendChild(tr);
  });
}

function filterWeapons() {
  const q = document.getElementById('weapon-search').value.toLowerCase();
  document.querySelectorAll('#weapon-table tbody tr').forEach(tr => {
    tr.classList.toggle('hidden', !!q && !tr.dataset.search.includes(q));
  });
}

function jumpToType(typeName) {
  showTab('types');
  document.getElementById('type-search').value = typeName;
  filterTypes();
  setTimeout(() => {
    const el = document.querySelector(`#types-panel .type-section[data-type="${CSS.escape(typeName)}"]`);
    if (el) { el.scrollIntoView({ behavior: 'smooth', block: 'start' }); }
  }, 50);
}

// ── Types ─────────────────────────────────────────────────────────────────────
function renderTypes() {
  const panel = document.getElementById('types-panel');
  Object.entries(__DATA__.types||{}).sort(([a],[b]) => a.localeCompare(b)).forEach(([typeName, weapons]) => {
    const div = document.createElement('div');
    div.className = 'type-section';
    div.dataset.type = typeName;
    div.dataset.search = typeName.toLowerCase();
    const items = weapons.map(w =>
      `<span class="weapon-link" onclick="jumpToWeapon('${esc(w.id)}')"
        title="${esc(w.id)}">${esc(w.name)}</span>`
    ).join(' &nbsp;·&nbsp; ');
    div.innerHTML = `
      <div class="type-heading">
        ${esc(typeName)} <span class="count-badge">${weapons.length}</span>
      </div>
      <div class="weapons-list">${items}</div>`;
    panel.appendChild(div);
  });
}

function filterTypes() {
  const q = document.getElementById('type-search').value.toLowerCase();
  document.querySelectorAll('#types-panel .type-section').forEach(sec => {
    sec.classList.toggle('hidden', !!q && !sec.dataset.search.includes(q));
  });
}

function jumpToWeapon(id) {
  showTab('weapons');
  document.getElementById('weapon-search').value = '';
  filterWeapons();
  setTimeout(() => {
    const el = document.getElementById('weapon-' + id);
    if (el) { el.scrollIntoView({ behavior: 'smooth', block: 'center' }); }
  }, 50);
}

// ── Attachments ──────────────────────────────────────────────────────────────

// A type with only one member is a leaf-adjacent `{directChildOf:X}` artifact
// (e.g. Stock → stock_xyz where each stock item becomes its own type). It is
// correct data but useless for browsing, so we suppress singleton types in
// both the Attachment Types tab and the Types chips on attachment rows.
function isMeaningfulAttachmentType(typeName) {
  const members = (__DATA__.attachmentTypes || {})[typeName];
  return !members || members.length >= 2;
}

function renderAttachments() {
  const tbody = document.getElementById('attachment-tbody');
  (__DATA__.attachments || []).forEach(a => {
    const types = (a.types || [])
      .filter(isMeaningfulAttachmentType)
      .map(t =>
        `<span class="type-tag" onclick="jumpToAttachmentType('${esc(t)}')">${esc(t)}</span>`
      ).join('');
    const tr = document.createElement('tr');
    tr.id = 'attachment-' + a.id;
    tr.dataset.search = `${a.name} ${a.id}`.toLowerCase();
    tr.innerHTML = `
      <td>
        ${esc(a.name)}<br>
        <span class="id-hover mono" title="${esc(a.id)}">${esc(a.id)}</span>
      </td>
      <td>${types}</td>`;
    tbody.appendChild(tr);
  });
}

function filterAttachments() {
  const q = document.getElementById('attachment-search').value.toLowerCase();
  document.querySelectorAll('#attachment-table tbody tr').forEach(tr => {
    tr.classList.toggle('hidden', !!q && !tr.dataset.search.includes(q));
  });
}

function jumpToAttachment(id) {
  showTab('attachments');
  document.getElementById('attachment-search').value = '';
  filterAttachments();
  setTimeout(() => {
    const el = document.getElementById('attachment-' + id);
    if (el) { el.scrollIntoView({ behavior: 'smooth', block: 'center' }); }
  }, 50);
}

// ── Attachment Types ─────────────────────────────────────────────────────────
function renderAttachmentTypes() {
  const panel = document.getElementById('attachment-types-panel');
  Object.entries(__DATA__.attachmentTypes || {})
    .filter(([_, attachments]) => attachments.length >= 2)
    .sort(([a], [b]) => a.localeCompare(b))
    .forEach(([typeName, attachments]) => {
      const div = document.createElement('div');
      div.className = 'type-section';
      div.dataset.type = typeName;
      div.dataset.search = typeName.toLowerCase();
      const items = attachments.map(a =>
        `<span class="weapon-link" onclick="jumpToAttachment('${esc(a.id)}')"
          title="${esc(a.id)}">${esc(a.name)}</span>`
      ).join(' &nbsp;·&nbsp; ');
      div.innerHTML = `
        <div class="type-heading">
          ${esc(typeName)} <span class="count-badge">${attachments.length}</span>
        </div>
        <div class="weapons-list">${items}</div>`;
      panel.appendChild(div);
    });
}

function filterAttachmentTypes() {
  const q = document.getElementById('attachment-type-search').value.toLowerCase();
  document.querySelectorAll('#attachment-types-panel .type-section').forEach(sec => {
    sec.classList.toggle('hidden', !!q && !sec.dataset.search.includes(q));
  });
}

function jumpToAttachmentType(typeName) {
  showTab('attachment-types');
  document.getElementById('attachment-type-search').value = typeName;
  filterAttachmentTypes();
  setTimeout(() => {
    const el = document.querySelector(
      `#attachment-types-panel .type-section[data-type="${CSS.escape(typeName)}"]`);
    if (el) { el.scrollIntoView({ behavior: 'smooth', block: 'start' }); }
  }, 50);
}

// ── Quests ────────────────────────────────────────────────────────────────────
function renderQuests() {
  const panel = document.getElementById('quests-panel');
  (__DATA__.quests||[]).forEach((q, qi) => {
    const isNoop = q.noop;
    const addedCount = (q.conditions||[]).reduce((n, c) => {
      const weaponAdded = (c.after||[]).filter(w => !(c.before||[]).find(b => b.id === w.id)).length;
      const inclAdded = countGroupAdditions(c.modsInclusiveBefore||[], c.modsInclusiveAfter||[]);
      const exclAdded = countGroupAdditions(c.modsExclusiveBefore||[], c.modsExclusiveAfter||[]);
      return n + weaponAdded + inclAdded + exclAdded;
    }, 0);
    const badge = isNoop
      ? '<span class="badge badge-noop">NOOP</span>'
      : `<span class="badge badge-expanded">+${addedCount}</span>`;

    const anyNextBest = (q.conditions||[]).find(c => c.nextBestType);
    const nextBestTag = anyNextBest
      ? `<span class="tag-amber" style="font-size:10px">next best: ${esc(anyNextBest.nextBestType)} +${anyNextBest.nextBestTypeCount}</span>`
      : '';

    const div = document.createElement('div');
    div.className = 'quest-row';
    div.dataset.noop = String(isNoop);
    div.dataset.search = `${q.name} ${q.id}`.toLowerCase();

    let condsHtml = '';
    (q.conditions||[]).forEach((c, ci) => {
      const matchTag = c.matchedType
        ? `<span class="type-tag" style="cursor:default">${esc(c.matchedType)}</span>` : '';
      const nbTag = c.nextBestType
        ? `<span class="tag-amber">next best: ${esc(c.nextBestType)} +${c.nextBestTypeCount}</span>` : '';
      const overrideTag = c.overrideMatched
        ? (() => {
            let label = `Mode: ${esc(c.expansionMode)}`;
            if ((c.overrideIncludedWeapons||[]).length) {
              label += ` (${(c.overrideIncludedWeapons).map(esc).join(', ')})`;
            }
            return `<span class="tag-amber" style="font-size:10px">${label}</span>`;
          })()
        : '';
      const modsOverrideTag = c.overrideMatched && (
        c.modsExpansionMode !== 'Auto' ||
        (c.overrideIncludedMods || []).length > 0 ||
        (c.overrideExcludedMods || []).length > 0)
        ? (() => {
            let label = `Mods: ${esc(c.modsExpansionMode)}`;
            const bits = [
              ...(c.overrideIncludedMods || []),
              ...((c.overrideExcludedMods || []).map(s => '-' + s))
            ];
            if (bits.length) {
              label += ` (${bits.map(esc).join(', ')})`;
            }
            return `<span class="tag-amber" style="font-size:10px">${label}</span>`;
          })()
        : '';

      let counterHtml = '';
      const fields = [];
      if (c.killCount != null)         { fields.push(`kills: <span>${c.killCount}</span>`); }
      if ((c.conditionLocation||[]).length) { fields.push(`location: <span>${(c.conditionLocation).map(esc).join(', ')}</span>`); }
      if ((c.enemyTypes||[]).length)   { fields.push(`enemy: <span>${(c.enemyTypes).map(esc).join(', ')}</span>`); }
      if (c.caliberFilter)             { fields.push(`caliber: <span class="mono">${esc(c.caliberFilter)}</span>`); }
      if (c.distance)                  { fields.push(`distance: <span>${esc(c.distance)}</span>`); }
      if (fields.length) {
        counterHtml = `<div class="counter-bar">${fields.map(f => `<div class="counter-field">${f}</div>`).join('')}</div>`;
      }

      const beforeSet = new Set((c.before||[]).map(w => w.id));
      const afterSet  = new Set((c.after ||[]).map(w => w.id));
      let diffHtml = '';
      if ((c.before||[]).length > 0 || (c.after||[]).length > 0) {
        diffHtml = '<div class="git-diff">';
        (c.before||[]).forEach(w => {
          if (!afterSet.has(w.id)) {
            diffHtml += `<div class="diff-removed">- ${esc(w.name)} <span class="id-hover" title="${esc(w.id)}">${esc(w.id)}</span></div>`;
          }
        });
        (c.after||[]).forEach(w => {
          if (beforeSet.has(w.id)) {
            diffHtml += `<div class="diff-same">  ${esc(w.name)} <span class="id-hover" title="${esc(w.id)}">${esc(w.id)}</span></div>`;
          } else {
            diffHtml += `<div class="diff-added">+ ${esc(w.name)} <span class="id-hover" title="${esc(w.id)}">${esc(w.id)}</span></div>`;
          }
        });
        diffHtml += '</div>';
      }

      const inclHtml = renderModGroupDiffs('mods include',
        c.modsInclusiveBefore || [], c.modsInclusiveAfter || []);
      const exclHtml = renderModGroupDiffs('mods exclude',
        c.modsExclusiveBefore || [], c.modsExclusiveAfter || []);

      const condDescHtml = c.description
        ? `<div class="cond-description">${esc(c.description)}</div>`
        : '';

      condsHtml += `
      <div class="cond-row">
        <div class="cond-header" onclick="toggleCondDetail(${qi},${ci})">
          <span class="cond-id" title="${esc(c.id)}">${c.id.length > 16 ? esc(c.id.slice(0,16)) + '…' : esc(c.id)}</span>
          ${matchTag}
          ${nbTag}
          ${overrideTag}
          ${modsOverrideTag}
          <span class="cond-chevron">▶</span>
        </div>
        <div class="cond-detail" id="cond-${qi}-${ci}">
          ${condDescHtml}
          ${counterHtml}
          ${diffHtml}
          ${inclHtml}
          ${exclHtml}
        </div>
      </div>`;
    });

    const metaFields = [];
    if (q.trader)    { metaFields.push(`<div class="meta-field"><div class="meta-label">Trader</div><div class="meta-value">${esc(q.trader)}</div></div>`); }
    if (q.location)  { metaFields.push(`<div class="meta-field"><div class="meta-label">Location</div><div class="meta-value">${esc(q.location)}</div></div>`); }
    if (q.questType) { metaFields.push(`<div class="meta-field"><div class="meta-label">Type</div><div class="meta-value">${esc(q.questType)}</div></div>`); }
    metaFields.push(`<div class="meta-field"><div class="meta-label">Quest ID</div><div class="meta-value id-hover mono" title="${esc(q.id)}">${esc(q.id)}</div></div>`);

    const questDescHtml = q.description
      ? `<div class="quest-description">${esc(q.description)}</div>`
      : '';

    div.innerHTML = `
      <div class="quest-header" onclick="toggleQuest(${qi})">
        ${badge}
        <span class="quest-name">${esc(q.name)}</span>
        ${nextBestTag}
        <span class="quest-chevron" id="qchev-${qi}">▶</span>
      </div>
      <div class="quest-meta" id="quest-meta-${qi}">${metaFields.join('')}${questDescHtml}</div>
      <div class="quest-conditions" id="quest-conds-${qi}">${condsHtml}</div>`;
    panel.appendChild(div);
  });
}

function toggleQuest(qi) {
  const meta  = document.getElementById('quest-meta-' + qi);
  const conds = document.getElementById('quest-conds-' + qi);
  const chev  = document.getElementById('qchev-' + qi);
  const open  = conds.classList.toggle('open');
  meta.classList.toggle('open', open);
  chev.textContent = open ? '▼' : '▶';
}

function toggleCondDetail(qi, ci) {
  const el   = document.getElementById(`cond-${qi}-${ci}`);
  const chev = el.previousElementSibling.querySelector('.cond-chevron');
  const open = el.classList.toggle('open');
  if (chev) { chev.textContent = open ? '▼' : '▶'; }
}

function filterQuests() {
  const q       = document.getElementById('quest-search').value.toLowerCase();
  const hideNoop = document.getElementById('hide-noop').checked;
  document.querySelectorAll('.quest-row').forEach(row => {
    const matchSearch = !q || row.dataset.search.includes(q);
    const isNoop      = row.dataset.noop === 'true';
    row.classList.toggle('hidden', !matchSearch || (hideNoop && isNoop));
  });
}

// ── Session state ─────────────────────────────────────────────────────────────
function persistSessionState() {
  const state = {
    activeTab: document.querySelector('.tab-btn.active')
      ?.getAttribute('onclick')?.match(/showTab\('(.+?)'\)/)?.[1] ?? 'settings',
    weaponSearch: document.getElementById('weapon-search')?.value ?? '',
    typeSearch: document.getElementById('type-search')?.value ?? '',
    questSearch: document.getElementById('quest-search')?.value ?? '',
    ruleSearch: document.getElementById('rule-search')?.value ?? '',
    hideNoop: document.getElementById('hide-noop')?.checked ?? true,
    attachmentSearch: document.getElementById('attachment-search')?.value ?? '',
    attachmentTypeSearch: document.getElementById('attachment-type-search')?.value ?? '',
    attachmentRuleSearch: document.getElementById('attachment-rule-search')?.value ?? '',
  };
  sessionStorage.setItem('mqw-inspector-state', JSON.stringify(state));
}

function hydrateSessionState() {
  let state;
  try { state = JSON.parse(sessionStorage.getItem('mqw-inspector-state') ?? 'null'); }
  catch { state = null; }
  if (!state) { return; }
  if (state.activeTab) { showTab(state.activeTab); }
  const setVal = (id, v) => { const el = document.getElementById(id); if (el && v != null) { el.value = v; } };
  setVal('weapon-search', state.weaponSearch);
  setVal('type-search', state.typeSearch);
  setVal('quest-search', state.questSearch);
  setVal('rule-search', state.ruleSearch);
  setVal('attachment-search', state.attachmentSearch);
  setVal('attachment-type-search', state.attachmentTypeSearch);
  setVal('attachment-rule-search', state.attachmentRuleSearch);
  const hn = document.getElementById('hide-noop');
  if (hn && typeof state.hideNoop === 'boolean') { hn.checked = state.hideNoop; }
  if (typeof filterWeapons === 'function') { filterWeapons(); }
  if (typeof filterTypes === 'function') { filterTypes(); }
  if (typeof filterQuests === 'function') { filterQuests(); }
  if (typeof filterRules === 'function') { filterRules(); }
  if (typeof filterAttachments === 'function') { filterAttachments(); }
  if (typeof filterAttachmentTypes === 'function') { filterAttachmentTypes(); }
  if (typeof filterAttachmentRules === 'function') { filterAttachmentRules(); }
}

// ── Entry point ───────────────────────────────────────────────────────────────
function renderInspector(data, root) {
  __DATA__ = data;
  const panelIds = [
    'settings-panel', 'weapon-tbody', 'types-panel',
    'attachment-tbody', 'attachment-types-panel', 'quests-panel'
  ];
  panelIds.forEach(id => {
    const el = document.getElementById(id);
    if (el) { el.innerHTML = ''; }
  });
  renderSettings();
  renderWeapons();
  renderTypes();
  renderAttachments();
  renderAttachmentTypes();
  renderQuests();
  filterQuests();
}
