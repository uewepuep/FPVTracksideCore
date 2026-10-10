// FPVTrackside API console. Everything it knows about the API comes from
// /api/v1/openapi.json, which the server builds from its own route table, so
// the console can't offer an endpoint or a field the server doesn't have.
// No libraries and no network beyond this server: venue laptops are often
// offline.
(function () {
  'use strict';

  var API = '/api/v1';
  var $ = function (sel, root) { return (root || document).querySelector(sel); };
  var esc = function (s) {
    return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
    });
  };
  var store = {
    get: function (k, d) { try { var v = localStorage.getItem('fpvapi.' + k); return v == null ? d : JSON.parse(v); } catch (e) { return d; } },
    set: function (k, v) { try { localStorage.setItem('fpvapi.' + k, JSON.stringify(v)); } catch (e) {} },
  };

  var doc = null;          // the OpenAPI document
  var ops = [];            // flattened operations
  var opsById = {};
  var live = { pilots: [], races: [], rounds: [], detections: {}, info: null, control: null };
  var current = null;      // { op, lastRequest, lastResponse }

  // ───────────────────────── HTTP ─────────────────────────

  function token() { return store.get('token', ''); }

  function headers(withBody) {
    var h = { 'Accept': 'application/json' };
    if (withBody) h['Content-Type'] = 'application/json';
    if (token()) h['Authorization'] = 'Bearer ' + token();
    return h;
  }

  function getJSON(path) {
    return fetch(path, { headers: headers(false), cache: 'no-store' }).then(function (r) {
      if (!r.ok) throw new Error(r.status + ' ' + r.statusText);
      return r.json();
    });
  }

  // ───────────────────────── live data (pickers + status) ─────────────────────────

  function refreshLists() {
    return Promise.all([
      getJSON(API + '/pilots').then(function (d) { live.pilots = d; }).catch(function () {}),
      getJSON(API + '/races').then(function (d) { live.races = d; }).catch(function () {}),
      getJSON(API + '/rounds').then(function (d) { live.rounds = d; }).catch(function () {}),
    ]).then(function () { live.detections = {}; });
  }

  function detectionsFor(raceId) {
    if (!raceId || !/^[0-9a-f-]{36}$/i.test(raceId)) return Promise.resolve([]);
    if (live.detections[raceId]) return Promise.resolve(live.detections[raceId]);
    return getJSON(API + '/races/' + raceId + '/detections').then(function (d) {
      live.detections[raceId] = d;
      return d;
    }).catch(function () { return []; });
  }

  function pilotName(id) {
    var p = live.pilots.find(function (x) { return x.id === id; });
    if (p) return p.name;
    for (var i = 0; i < live.races.length; i++) {
      var s = live.races[i].pilots.find(function (x) { return x.pilotId === id; });
      if (s) return s.name;
    }
    return id ? id.slice(0, 8) : '';
  }

  function fmtClock(sec) {
    var m = Math.floor(sec / 60), s = sec - m * 60;
    return (m < 10 ? '0' : '') + m + ':' + (s < 10 ? '0' : '') + s.toFixed(1);
  }

  function pollStatus() {
    Promise.all([getJSON(API).catch(function () { return null; }), getJSON(API + '/race-control').catch(function () { return null; })])
      .then(function (r) {
        var info = r[0], ctl = r[1];
        var ev = $('#eventPill'), race = $('#racePill'), write = $('#writePill');
        if (!info) {
          ev.className = 'pill bad'; ev.innerHTML = '<span class="dot"></span>FPVTrackside not reachable';
          race.hidden = true; write.hidden = true;
          return;
        }
        var changed = live.control && ctl && (live.control.state !== ctl.state || (live.control.currentRace || {}).id !== (ctl.currentRace || {}).id);
        live.info = info; live.control = ctl;
        ev.className = 'pill ok';
        ev.innerHTML = '<span class="dot"></span>' + esc(info.eventName || 'No event') + ' <span class="muted">· v' + esc(info.version || '?') + '</span>';
        ev.title = 'Event ' + info.eventId;

        if (ctl) {
          race.hidden = false;
          var name = ctl.currentRace ? ctl.currentRace.name : 'No race selected';
          var cls = { running: 'live', starting: 'warn', finished: 'ok', ready: '', none: '' }[ctl.state] || '';
          var extra = ctl.state === 'running' ? ' · ' + fmtClock(ctl.elapsedSeconds) : ' · ' + ctl.state;
          race.className = 'pill ' + cls;
          race.innerHTML = '<span class="dot"></span>' + esc(name) + esc(extra);
        }
        write.hidden = false;
        write.className = 'pill ' + (info.canWrite ? 'ok' : 'warn');
        write.innerHTML = '<span class="dot"></span>' + (info.canWrite ? 'Changes allowed' : 'Read only — add a token');
        write.title = info.canWrite ? 'This browser may make changes.' : 'Reads work. To make changes from this computer, add the API token (🔑).';
        if (changed) refreshLists().then(refreshPickers);
      });
  }

  // ───────────────────────── OpenAPI helpers ─────────────────────────

  function refName(s) { return s && s.$ref ? s.$ref.split('/').pop() : null; }
  function schemaOf(s) { var n = refName(s); return n ? doc.components.schemas[n] : s; }
  function typeLabel(s) {
    if (!s) return '';
    if (s.allOf) return typeLabel(s.allOf[0]);
    var n = refName(s);
    if (n) return '<a href="#/models/' + esc(n) + '">' + esc(n) + '</a>';
    if (s.type === 'array') return typeLabel(s.items) + '[]';
    return esc(s.type + (s.format ? ' · ' + s.format : '')) + (s.nullable ? ' <span class="muted">or null</span>' : '');
  }

  function fieldsTable(s) {
    var m = schemaOf(s);
    if (!m || !m.properties) return '<p class="muted">No fields.</p>';
    var req = m.required || [];
    var rows = Object.keys(m.properties).map(function (k) {
      var p = m.properties[k];
      return '<tr><td class="f">' + esc(k) + (req.indexOf(k) >= 0 ? ' <span class="req" title="required">*</span>' : '') + '</td>'
        + '<td class="t">' + typeLabel(p) + '</td><td>' + esc(p.description || '')
        + (p['enum'] ? '<div class="enum">One of: ' + p['enum'].map(function (v) { return '<code>' + esc(v) + '</code>'; }).join(' ') + '</div>' : '')
        + (p.example ? '<div class="enum">e.g. <code>' + esc(p.example) + '</code></div>' : '') + '</td></tr>';
    }).join('');
    return '<table class="fields"><tr><th>Field</th><th>Type</th><th>Meaning</th></tr>' + rows + '</table>';
  }

  function bodySchema(op) {
    return op.requestBody && op.requestBody.content['application/json'].schema;
  }
  function bodyExample(op) {
    return op.requestBody && op.requestBody.content['application/json'].example;
  }
  function okResponse(op) {
    var code = Object.keys(op.responses).filter(function (c) { return c !== 'default'; })[0];
    var c = op.responses[code].content;
    return { code: code, schema: c && c['application/json'].schema };
  }

  // A starter body when the server doesn't give one: required fields only.
  function skeleton(s) {
    if (!s) return null;
    if (s.type === 'array') return [skeleton(s.items)];
    var m = schemaOf(s);
    if (!m || !m.properties) return {};
    var out = {};
    (m.required || []).forEach(function (k) {
      var p = m.properties[k];
      out[k] = p.example || (p.format === 'uuid' ? '' : p.type === 'boolean' ? false : p.type === 'integer' || p.type === 'number' ? 0 : '');
    });
    return out;
  }

  // ───────────────────────── layout: nav ─────────────────────────

  function buildNav(filter) {
    var q = (filter || '').trim().toLowerCase();
    var html = '';
    if (!q) {
      html += '<div class="nav-group"><a class="nav-item" href="#/" data-route="/"><span class="label">🏁 Getting started</span></a>'
        + '<a class="nav-item" href="#/history" data-route="/history"><span class="label">🕘 History</span></a>'
        + '<a class="nav-item" href="#/models" data-route="/models"><span class="label">📦 Models</span></a></div>';
    }
    doc.tags.forEach(function (t) {
      var list = ops.filter(function (o) {
        if (o.tag !== t.name) return false;
        if (!q) return true;
        return (o.method + ' ' + o.path + ' ' + o.op.summary + ' ' + (o.op.description || '') + ' ' + o.tag).toLowerCase().indexOf(q) >= 0;
      });
      if (!list.length) return;
      html += '<div class="nav-group"><div class="nav-title">' + esc(t.name) + '</div>';
      list.forEach(function (o) {
        html += '<a class="nav-item" href="#/op/' + esc(o.id) + '" data-route="/op/' + esc(o.id) + '" title="' + esc(o.method + ' ' + o.path) + '">'
          + '<span class="m ' + o.method + '">' + o.method + '</span><span class="label">' + esc(o.op.summary) + '</span></a>';
      });
      html += '</div>';
    });
    if (!html) html = '<p class="muted" style="padding:10px">Nothing matches “' + esc(filter) + '”.</p>';
    $('#nav').innerHTML = html;
    markNav();
  }

  function markNav() {
    var route = location.hash.replace(/^#/, '') || '/';
    document.querySelectorAll('.nav-item').forEach(function (a) {
      a.classList.toggle('active', a.dataset.route === route);
      if (a.dataset.route === route) a.setAttribute('aria-current', 'page'); else a.removeAttribute('aria-current');
    });
  }

  // ───────────────────────── views ─────────────────────────

  function route() {
    var h = location.hash.replace(/^#/, '') || '/';
    // A search that hides where we're going would leave the sidebar showing
    // something unrelated; clear it.
    if ($('#search').value && !document.querySelector('.nav-item[data-route="' + h.replace(/"/g, '') + '"]')) {
      $('#search').value = '';
      buildNav('');
    }
    markNav();
    $('#side').classList.remove('open');
    $('#menuBtn').setAttribute('aria-expanded', 'false');
    var m;
    if ((m = h.match(/^\/op\/([^/?]+)/)) && opsById[m[1]]) return showOp(opsById[m[1]]);
    if ((m = h.match(/^\/models\/(.+)$/))) return showModels(decodeURIComponent(m[1]));
    if (h === '/models') return showModels();
    if (h === '/history') return showHistory();
    return showHome();
  }

  function link(id, text) {
    var o = opsById[id];
    if (!o) return esc(text || id);
    return '<a href="#/op/' + esc(id) + '"><span class="m ' + o.method + '" style="width:auto;padding:1px 6px">' + o.method + '</span> ' + esc(text || o.op.summary) + '</a>';
  }

  function showHome() {
    var origin = location.origin;
    $('#main').innerHTML =
      '<section class="hero"><h1>FPVTrackside API</h1>'
      + '<p>Everything the race director can do, over HTTP: read the event, its pilots, rounds, races, laps and every timing-gate crossing; '
      + 'build heats; add or remove laps; run races. Changes show up on the FPVTrackside screen straight away.</p>'
      + '<p>Pick an endpoint on the left, press <b>Send</b>, and see exactly what comes back. Requests run against <b>this</b> FPVTrackside — '
      + 'the event that is open right now — so use the sandbox or a copy of an event to experiment.</p>'
      + '<div class="send-row"><a class="btn" href="#/op/getRaceControl">Try it: what\'s happening now →</a>'
      + '<a class="btn ghost" href="' + API + '/openapi.json" download="fpvtrackside-openapi.json">⬇ OpenAPI document</a></div></section>'

      + '<h2>The basics</h2><div class="facts">'
      + '<div class="fact"><b>Base URL</b><code>' + esc(origin + API) + '</code></div>'
      + '<div class="fact"><b>JSON in and out</b>camelCase fields; errors are <code>{"ok":false,"code":"…","error":"…"}</code> with an HTTP status.</div>'
      + '<div class="fact"><b>IDs</b>GUIDs. Pilots can also be named (<code>Wheelzfpv</code>), channels by label (<code>R1</code>) or frequency (<code>5658</code>).</div>'
      + '<div class="fact"><b>Times</b>ISO 8601 in the FPVTrackside machine\'s local time with its offset. <code>raceTimeMs</code> counts from the race start.</div>'
      + '<div class="fact"><b>Reading</b>Open to anyone who can reach this machine on the network.</div>'
      + '<div class="fact"><b>Changing</b>Allowed from this machine, or from elsewhere with <code>Authorization: Bearer &lt;FPV_API_TOKEN&gt;</code> (🔑 above).</div>'
      + '</div>'

      + '<h2>Recipes</h2><div class="cards">'
      + '<div class="card"><h3>Fix a lap the timer got wrong</h3><p class="muted">What the AUFPV lap review sends through dd-pits.</p><ol>'
      + '<li>' + link('getRacesByRaceIdDetections', 'List the heat\'s crossings') + '</li>'
      + '<li>' + link('postRacesByRaceIdLapCorrections', 'Send the correction') + '</li>'
      + '<li>' + link('getRacesByRaceIdResults', 'Check the results moved') + '</li></ol></div>'
      + '<div class="card"><h3>Run a race</h3><ol>'
      + '<li>' + link('putRaceControlCurrent', 'Select the race') + '</li>'
      + '<li>' + link('postRaceControlStart', 'Start it') + '</li>'
      + '<li>' + link('getRaceControl', 'Watch the clock') + '</li>'
      + '<li>' + link('postRaceControlStop', 'Stop it') + '</li></ol></div>'
      + '<div class="card"><h3>Build a heat</h3><ol>'
      + '<li>' + link('getPilots', 'See who\'s in the event') + '</li>'
      + '<li>' + link('postRaces', 'Create the race with its line-up') + '</li>'
      + '<li>' + link('postRacesByRaceIdPilots', 'Add a late pilot') + '</li></ol></div>'
      + '<div class="card"><h3>Add a lap by hand</h3><ol>'
      + '<li>' + link('getRacesByRaceIdLaps', 'Look at the pilot\'s laps') + '</li>'
      + '<li>' + link('postRacesByRaceIdLaps', 'Add the missing lap line') + '</li>'
      + '<li>' + link('patchRacesByRaceIdDetectionsByDetectionId', 'Or discount a false one') + '</li></ol></div>'
      + '</div>'

      + '<h2>From code</h2><pre class="code">' + esc(
          'curl ' + origin + API + '/race-control\n\n'
        + 'curl -X POST ' + origin + API + '/races/<raceId>/lap-corrections \\\n'
        + '  -H "Content-Type: application/json" \\\n'
        + '  -d \'{"pilotId":"<pilotId>","invalidate":["<detectionId>"]}\'') + '</pre>'
      + '<p class="muted">Every endpoint page has a <b>Code</b> tab with the exact request in curl, JavaScript, Python and Go.</p>';
    $('#main').focus();
  }

  // ───────────────────────── operation view ─────────────────────────

  var pickerKinds = { raceId: 'race', pilotId: 'pilot', roundId: 'round', detectionId: 'detection', round: 'round', pilot: 'pilot', state: 'state' };

  function defaultRace(o) {
    var finished = live.races.filter(function (r) { return r.state === 'finished'; });
    var lapish = /laps|detections|lap-corrections|results/.test(o.path);
    var cur = live.races.find(function (r) { return r.isCurrent; });
    if (lapish && finished.length) return finished[0].id;
    if (/race-control\/current/.test(o.path)) return (live.races.find(function (r) { return r.state === 'pending' && !r.isCurrent; }) || live.races[0] || {}).id;
    return (cur || live.races[0] || {}).id;
  }

  function options(kind, value, optional, raceId) {
    var list = [], html = '';
    if (kind === 'race') list = live.races.map(function (r) { return [r.id, r.name + ' · ' + r.state + ' · ' + r.pilots.length + ' pilots' + (r.isCurrent ? ' · selected' : '')]; });
    if (kind === 'round') list = live.rounds.map(function (r) { return [r.id, (r.name || r.eventType + ' round ' + r.number) + ' · ' + r.raceCount + ' races']; });
    if (kind === 'state') list = [['pending', 'pending'], ['running', 'running'], ['finished', 'finished']];
    if (kind === 'pilot') {
      var race = raceId && live.races.find(function (r) { return r.id === raceId; });
      list = (race ? race.pilots.map(function (p) { return [p.pilotId, p.name + ' · ' + p.channel.label]; }) : [])
        .concat(live.pilots.filter(function (p) { return !race || !race.pilots.some(function (x) { return x.pilotId === p.id; }); })
          .map(function (p) { return [p.id, p.name + (race ? ' · not in this race' : '')]; }));
    }
    if (optional) html += '<option value="">— any —</option>';
    list.forEach(function (x) { html += '<option value="' + esc(x[0]) + '"' + (x[0] === value ? ' selected' : '') + '>' + esc(x[1]) + '</option>'; });
    html += '<option value="__custom">Type a value…</option>';
    return html;
  }

  function detectionOptions(dets, value) {
    var html = '';
    dets.forEach(function (d) {
      var label = pilotName(d.pilotId) + ' · ' + (d.isLapEnd ? 'lap line' : 'gate ' + d.timingSystemIndex) + ' · ' + (d.raceTimeMs / 1000).toFixed(3) + 's' + (d.valid ? '' : ' · invalid');
      html += '<option value="' + esc(d.id) + '"' + (d.id === value ? ' selected' : '') + '>' + esc(label) + '</option>';
    });
    if (!dets.length) html += '<option value="">No crossings in this race</option>';
    return html + '<option value="__custom">Type a value…</option>';
  }

  function paramField(p, o) {
    var kind = pickerKinds[p.name];
    var opt = !p.required;
    var id = 'p-' + p.name;
    var control;
    if (kind) {
      control = '<select id="' + id + '" data-param="' + esc(p.name) + '" data-in="' + p['in'] + '" data-kind="' + kind + '"></select>'
        + '<input type="text" id="' + id + '-custom" data-custom-for="' + esc(p.name) + '" placeholder="' + esc(p.name) + '" hidden>';
    } else {
      control = '<input type="text" id="' + id + '" data-param="' + esc(p.name) + '" data-in="' + p['in'] + '" placeholder="' + esc(p.name) + '">';
    }
    return '<div class="field"><label for="' + id + '">' + esc(p.name) + (opt ? '' : ' <span class="req">*</span>') + '<small>' + esc(p['in']) + '</small></label>'
      + '<div><div class="control">' + control + '</div><div class="hint">' + esc(p.description || '') + '</div></div></div>';
  }

  function paramValue(name) {
    var el = $('[data-param="' + name + '"]');
    if (!el) return '';
    if (el.tagName === 'SELECT' && el.value === '__custom') return ($('[data-custom-for="' + name + '"]').value || '').trim();
    return (el.value || '').trim();
  }

  function refreshPickers() {
    if (!current) return;
    var o = current.op;
    var raceSel = $('[data-param="raceId"]');
    document.querySelectorAll('select[data-kind]').forEach(function (sel) {
      var kind = sel.dataset.kind, name = sel.dataset.param;
      if (kind === 'detection') return;
      var keep = sel.value && sel.value !== '__custom' ? sel.value : null;
      var optional = sel.dataset.in === 'query';
      var value = keep;
      if (!value && !optional) {
        if (kind === 'race') value = defaultRace(o);
        if (kind === 'round') value = (live.rounds[0] || {}).id;
        if (kind === 'pilot') {
          var r = raceSel && live.races.find(function (x) { return x.id === raceSel.value; });
          value = r && r.pilots[0] ? r.pilots[0].pilotId : (live.pilots[0] || {}).id;
        }
      }
      sel.innerHTML = options(kind, value, optional, kind === 'pilot' && raceSel ? raceSel.value : null);
    });
    var detSel = $('select[data-kind="detection"]');
    if (detSel) {
      detectionsFor(paramValue('raceId')).then(function (dets) {
        var keep = detSel.value && detSel.value !== '__custom' ? detSel.value : null;
        var pick = keep || (dets.find(function (d) { return d.isLapEnd && d.lapNumber > 0; }) || dets[0] || {}).id;
        detSel.innerHTML = detectionOptions(dets, pick);
        updateUrlPreview();
      });
    }
    updateUrlPreview();
  }

  // Fills {{placeholders}} in an example body from the live event: the race
  // picked above (or the selected/finished one), one of its pilots, and that
  // pilot's crossings.
  function resolveExample(text) {
    if (!text || text.indexOf('{{') < 0) return Promise.resolve(text);
    var raceId = paramValue('raceId') || defaultRace(current.op) || '';
    var race = live.races.find(function (r) { return r.id === raceId; }) || live.races[0] || { pilots: [] };
    var pending = live.races.find(function (r) { return r.state === 'pending' && !r.isCurrent; }) || race;
    return detectionsFor(race.id).then(function (dets) {
      var pilot = race.pilots.find(function (p) { return dets.some(function (d) { return d.pilotId === p.pilotId && !d.isLapEnd; }); })
        || race.pilots[0] || { pilotId: (live.pilots[0] || {}).id, name: (live.pilots[0] || {}).name };
      var mine = dets.filter(function (d) { return d.pilotId === pilot.pilotId; });
      var split = mine.find(function (d) { return !d.isLapEnd; }) || {};
      var lines = mine.filter(function (d) { return d.isLapEnd && d.valid; });
      var lapLine = lines[2] || lines[1] || lines[0] || {};
      var values = {
        'pilot.id': pilot.pilotId || '', 'pilot.name': pilot.name || '',
        'race.id': pending.id || '', 'round.id': (live.rounds[live.rounds.length - 1] || {}).id || '',
        'split.id': split.id || '', 'lapLine.id': lapLine.id || '',
      };
      return text.replace(/\{\{([a-zA-Z.]+)\}\}/g, function (m, k) { return values[k] != null ? values[k] : m; });
    });
  }

  function showOp(o) {
    var op = o.op;
    var body = bodySchema(op);
    var ok = okResponse(op);
    var params = op.parameters || [];
    current = { op: o, lastRequest: null, lastResponse: null, bodyTouched: false };

    var html = '<div class="op-head"><span class="m ' + o.method + '">' + o.method + '</span><h1>' + esc(op.summary) + '</h1>'
      + (op.security ? '<span class="badge write" title="Changes FPVTrackside data">✎ changes data</span>' : '<span class="badge">read only</span>') + '</div>'
      + '<div class="op-path"><code style="background:none;padding:0">' + esc(o.path) + '</code></div>'
      + (op.description ? '<p class="desc">' + esc(op.description) + '</p>' : '')
      + '<div class="op-grid">'

      // request
      + '<section class="panel" aria-label="Request"><div class="panel-head">Request<span class="grow"></span>'
      + '<button class="btn ghost small" id="resetBtn" title="Reset the form to its example">↺ Reset</button></div><div class="panel-body">'
      + (params.length ? params.map(function (p) { return paramField(p, o); }).join('') : '<p class="muted">No parameters.</p>')
      + (body ? '<h3>Body <span class="muted" style="font-weight:400">' + typeLabel(body) + '</span></h3>'
        + '<div class="editor"><textarea id="body" spellcheck="false" aria-label="Request body (JSON)"></textarea></div>'
        + '<div class="editor-bar"><span class="json-state" id="jsonState"></span><span style="flex:1"></span>'
        + '<button class="btn ghost small" id="fmtBtn">Format</button><button class="btn ghost small" id="exBtn">Example</button></div>' : '')
      + '<div class="send-row"><button class="btn" id="sendBtn">Send ' + o.method + '</button><span class="kbd">Ctrl + Enter</span></div>'
      + '<div class="url-preview" id="urlPreview"></div>'
      + '</div></section>'

      // response
      + '<section class="panel" aria-label="Response"><div class="tabs" role="tablist">'
      + tabBtn('res', 'Response', true) + tabBtn('hdr', 'Headers') + tabBtn('code', 'Code') + tabBtn('schema', 'Schema')
      + '</div><div class="panel-body">'
      + '<div data-pane="res"><div class="empty">Press <b>Send</b> to see the response.</div></div>'
      + '<div data-pane="hdr" hidden><div class="empty">No response yet.</div></div>'
      + '<div data-pane="code" hidden></div>'
      + '<div data-pane="schema" hidden>'
      + (body ? '<h3>Request body</h3>' + fieldsTable(body.items || body) : '')
      + '<h3>Response ' + esc(ok.code) + (ok.schema ? ' <span class="muted" style="font-weight:400">' + typeLabel(ok.schema) + '</span>' : '') + '</h3>'
      + (ok.schema ? fieldsTable(ok.schema.items || ok.schema) : '<p class="muted">No body.</p>')
      + '<h3>Errors</h3>' + fieldsTable({ $ref: '#/components/schemas/ApiErrorDto' })
      + '</div></div></section></div>';

    $('#main').innerHTML = html;
    $('#main').scrollTop = 0;

    document.querySelectorAll('.tab').forEach(function (t) { t.addEventListener('click', function () { selectTab(t.dataset.tab); }); });
    document.querySelectorAll('[data-param]').forEach(function (el) {
      el.addEventListener('change', function () {
        if (el.tagName === 'SELECT') {
          var custom = $('[data-custom-for="' + el.dataset.param + '"]');
          if (custom) { custom.hidden = el.value !== '__custom'; if (!custom.hidden) custom.focus(); }
          if (el.dataset.param === 'raceId') {
            document.querySelectorAll('select[data-kind="pilot"],select[data-kind="detection"]').forEach(function (s) { s.value = ''; });
            refreshPickers();
            if (!current.bodyTouched) loadExample();
          }
        }
        updateUrlPreview();
      });
      el.addEventListener('input', updateUrlPreview);
    });
    document.querySelectorAll('[data-custom-for]').forEach(function (el) { el.addEventListener('input', updateUrlPreview); });

    if (body) {
      var ta = $('#body');
      ta.addEventListener('input', function () { current.bodyTouched = true; checkJSON(); });
      ta.addEventListener('keydown', function (e) {
        if (e.key === 'Tab' && !e.shiftKey) { e.preventDefault(); var s = ta.selectionStart; ta.setRangeText('  ', s, ta.selectionEnd, 'end'); }
      });
      $('#fmtBtn').addEventListener('click', function () { try { ta.value = JSON.stringify(JSON.parse(ta.value), null, 2); checkJSON(); } catch (e) {} });
      $('#exBtn').addEventListener('click', function () { current.bodyTouched = false; loadExample(); });
    }
    $('#resetBtn').addEventListener('click', function () { showOp(o); });
    $('#sendBtn').addEventListener('click', send);

    refreshPickers();
    if (body) loadExample();
    renderCode();
  }

  function tabBtn(id, label, selected) {
    return '<button class="tab" role="tab" data-tab="' + id + '" aria-selected="' + (selected ? 'true' : 'false') + '">' + label + '</button>';
  }

  function selectTab(id) {
    document.querySelectorAll('.tab').forEach(function (t) { t.setAttribute('aria-selected', t.dataset.tab === id ? 'true' : 'false'); });
    document.querySelectorAll('[data-pane]').forEach(function (p) { p.hidden = p.dataset.pane !== id; });
    if (id === 'code') renderCode();
  }

  function loadExample() {
    var op = current.op.op;
    var ex = bodyExample(op);
    var text = ex != null ? JSON.stringify(ex, null, 2) : JSON.stringify(skeleton(bodySchema(op)), null, 2);
    resolveExample(text).then(function (t) {
      var ta = $('#body');
      if (!ta) return;
      ta.value = t;
      checkJSON();
      renderCode();
    });
  }

  function checkJSON() {
    var ta = $('#body'), st = $('#jsonState');
    if (!ta) return true;
    try {
      if (ta.value.trim()) JSON.parse(ta.value);
      st.className = 'json-state ok'; st.textContent = '✓ valid JSON';
      ta.removeAttribute('aria-invalid');
      return true;
    } catch (e) {
      st.className = 'json-state bad'; st.textContent = '✗ ' + e.message;
      ta.setAttribute('aria-invalid', 'true');
      return false;
    }
  }

  function buildRequest() {
    var o = current.op;
    var missing = [];
    var path = o.path.replace(/\{([^}]+)\}/g, function (m, name) {
      var v = paramValue(name);
      if (!v) missing.push(name);
      return encodeURIComponent(v || m);
    });
    var qs = (o.op.parameters || []).filter(function (p) { return p['in'] === 'query'; })
      .map(function (p) { var v = paramValue(p.name); return v ? encodeURIComponent(p.name) + '=' + encodeURIComponent(v) : null; })
      .filter(Boolean).join('&');
    var ta = $('#body');
    var hasBody = o.method !== 'GET';
    var body = hasBody ? (ta ? ta.value : '{}') : null;
    if (hasBody && !String(body).trim()) body = '{}';
    return { method: o.method, path: path + (qs ? '?' + qs : ''), url: location.origin + path + (qs ? '?' + qs : ''), body: body, missing: missing, opId: o.id };
  }

  function updateUrlPreview() {
    if (!current) return;
    var r = buildRequest();
    var el = $('#urlPreview');
    if (el) el.textContent = r.method + ' ' + r.url;
    if ($('[data-pane="code"]') && !$('[data-pane="code"]').hidden) renderCode();
  }

  function send() {
    if (!current) return;
    if (!checkJSON()) { toast('Fix the JSON first'); $('#body').focus(); return; }
    var req = buildRequest();
    if (req.missing.length) { toast('Fill in ' + req.missing.join(', ')); return; }
    var btn = $('#sendBtn');
    btn.disabled = true; btn.textContent = 'Sending…';
    var t0 = performance.now();
    var init = { method: req.method, headers: headers(req.body != null), cache: 'no-store' };
    if (req.body != null) init.body = req.body;
    current.lastRequest = req;

    fetch(req.path, init).then(function (res) {
      return res.text().then(function (text) {
        return { status: res.status, statusText: res.statusText, headers: res.headers, text: text, ms: performance.now() - t0 };
      });
    }).catch(function (e) {
      return { status: 0, statusText: 'Network error', headers: new Headers(), text: String(e), ms: performance.now() - t0 };
    }).then(function (r) {
      btn.disabled = false; btn.textContent = 'Send ' + req.method;
      current.lastResponse = r;
      renderResponse(r);
      remember(req, r);
      if (req.method !== 'GET' && r.status >= 200 && r.status < 300) {
        refreshLists().then(refreshPickers);
        pollStatus();
      }
    });
  }

  function renderResponse(r) {
    var cls = 's' + String(r.status).charAt(0);
    var pretty = r.text;
    var isJSON = false;
    try { pretty = JSON.stringify(JSON.parse(r.text), null, 2); isJSON = true; } catch (e) {}
    var size = new Blob([r.text]).size;
    var hint = '';
    if (r.status === 401 || r.status === 403) hint = '<p class="muted">Changes from another computer need the API token — press 🔑 Token at the top.</p>';
    if (r.status === 0) hint = '<p class="muted">FPVTrackside didn\'t answer. Is it still running?</p>';
    $('[data-pane="res"]').innerHTML =
      '<div class="res-meta"><span class="code-status ' + cls + '">' + (r.status || 'ERR') + '</span><span>' + esc(r.statusText) + '</span>'
      + '<span>' + Math.round(r.ms) + ' ms</span><span>' + (size > 1024 ? (size / 1024).toFixed(1) + ' KB' : size + ' B') + '</span>'
      + '<span style="flex:1"></span><button class="btn ghost small" id="copyRes">Copy</button></div>' + hint
      + '<pre class="code" style="margin-top:10px">' + (isJSON ? highlightJSON(pretty) : esc(pretty || '(empty)')) + '</pre>';
    $('#copyRes').addEventListener('click', function () { copy(pretty); });
    var hdrs = '';
    r.headers.forEach(function (v, k) { hdrs += '<tr><td class="f">' + esc(k) + '</td><td>' + esc(v) + '</td></tr>'; });
    $('[data-pane="hdr"]').innerHTML = hdrs ? '<table class="fields"><tr><th>Header</th><th>Value</th></tr>' + hdrs + '</table>' : '<div class="empty">No headers.</div>';
    selectTab('res');
  }

  function highlightJSON(s) {
    return esc(s).replace(/(&quot;(?:\\.|[^\\&]|&(?!quot;))*?&quot;)(\s*:)?|\b(true|false)\b|\bnull\b|-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?/g, function (m, str, colon, bool) {
      if (str) return colon ? '<span class="j-k">' + str + '</span>' + colon : '<span class="j-s">' + str + '</span>';
      if (bool) return '<span class="j-b">' + m + '</span>';
      if (m === 'null') return '<span class="j-z">null</span>';
      return '<span class="j-n">' + m + '</span>';
    });
  }

  // ───────────────────────── code snippets ─────────────────────────

  var langs = [['curl', 'curl'], ['js', 'JavaScript'], ['py', 'Python'], ['go', 'Go']];

  function snippet(lang, r) {
    var auth = token() ? 'Bearer ' + token() : null;
    var bodyLit = r.body;
    try { bodyLit = JSON.stringify(JSON.parse(r.body)); } catch (e) {}
    if (lang === 'curl') {
      return 'curl -X ' + r.method + " '" + r.url + "'"
        + (auth ? " \\\n  -H 'Authorization: " + auth + "'" : '')
        + (r.body != null ? " \\\n  -H 'Content-Type: application/json' \\\n  -d '" + String(bodyLit).replace(/'/g, "'\\''") + "'" : '');
    }
    if (lang === 'js') {
      var h = { 'Accept': 'application/json' };
      if (r.body != null) h['Content-Type'] = 'application/json';
      if (auth) h['Authorization'] = auth;
      return 'const res = await fetch(' + JSON.stringify(r.url) + ', {\n  method: ' + JSON.stringify(r.method) + ',\n  headers: ' + JSON.stringify(h, null, 2).replace(/\n/g, '\n  ')
        + (r.body != null ? ',\n  body: JSON.stringify(' + (r.body.trim() ? r.body.trim().replace(/\n/g, '\n  ') : '{}') + ')' : '')
        + '\n});\nconst data = await res.json();\nif (!res.ok) throw new Error(data.error);\nconsole.log(data);';
    }
    if (lang === 'py') {
      var py = 'import requests\n\nres = requests.request(\n    ' + JSON.stringify(r.method) + ',\n    ' + JSON.stringify(r.url);
      if (auth) py += ',\n    headers={"Authorization": ' + JSON.stringify(auth) + '}';
      if (r.body != null) {
        var pyBody = r.body;
        try { pyBody = JSON.stringify(JSON.parse(r.body), null, 4).replace(/\btrue\b/g, 'True').replace(/\bfalse\b/g, 'False').replace(/\bnull\b/g, 'None').replace(/\n/g, '\n    '); } catch (e) {}
        py += ',\n    json=' + pyBody;
      }
      return py + ',\n    timeout=20,\n)\nres.raise_for_status()\nprint(res.json())';
    }
    if (lang === 'go') {
      return 'body := strings.NewReader(`' + (r.body != null ? bodyLit : '') + '`)\n'
        + 'req, err := http.NewRequestWithContext(ctx, ' + JSON.stringify(r.method) + ', ' + JSON.stringify(r.url) + ', body)\n'
        + 'if err != nil {\n\treturn err\n}\n'
        + (r.body != null ? 'req.Header.Set("Content-Type", "application/json")\n' : '')
        + (auth ? 'req.Header.Set("Authorization", ' + JSON.stringify(auth) + ')\n' : '')
        + 'res, err := http.DefaultClient.Do(req)\nif err != nil {\n\treturn err\n}\ndefer res.Body.Close()\n'
        + 'var out map[string]any\nif err := json.NewDecoder(res.Body).Decode(&out); err != nil {\n\treturn err\n}\n'
        + 'if res.StatusCode >= 300 {\n\treturn fmt.Errorf("fpvtrackside: %v", out["error"])\n}';
    }
    return '';
  }

  function renderCode() {
    var pane = $('[data-pane="code"]');
    if (!pane || !current) return;
    var lang = store.get('lang', 'curl');
    var r = buildRequest();
    pane.innerHTML = '<div class="copy-row"><div class="seg" role="group" aria-label="Language">'
      + langs.map(function (l) { return '<button data-lang="' + l[0] + '" aria-pressed="' + (l[0] === lang) + '">' + l[1] + '</button>'; }).join('')
      + '</div><span style="flex:1"></span><button class="btn ghost small" id="copyCode">Copy</button></div>'
      + '<pre class="code">' + esc(snippet(lang, r)) + '</pre>'
      + (token() ? '<p class="muted" style="font-size:12.5px">Includes your saved token — keep snippets private.</p>' : '');
    pane.querySelectorAll('[data-lang]').forEach(function (b) { b.addEventListener('click', function () { store.set('lang', b.dataset.lang); renderCode(); }); });
    $('#copyCode').addEventListener('click', function () { copy(snippet(lang, r)); });
  }

  // ───────────────────────── history ─────────────────────────

  function remember(req, r) {
    var h = store.get('history', []);
    h.unshift({ opId: req.opId, method: req.method, path: req.path, body: req.body, status: r.status, ms: Math.round(r.ms), at: Date.now() });
    store.set('history', h.slice(0, 50));
  }

  function showHistory() {
    current = null;
    var h = store.get('history', []);
    var rows = h.map(function (x, i) {
      var cls = 's' + String(x.status).charAt(0);
      return '<div class="history-item" data-i="' + i + '" tabindex="0" role="button"><span class="m ' + x.method + '">' + x.method + '</span>'
        + '<span class="code-status ' + cls + '">' + (x.status || 'ERR') + '</span><span class="u" title="' + esc(x.path) + '">' + esc(x.path) + '</span>'
        + '<span class="when">' + new Date(x.at).toLocaleTimeString() + ' · ' + x.ms + ' ms</span></div>';
    }).join('');
    $('#main').innerHTML = '<div class="op-head"><h1>History</h1><span style="flex:1"></span>' + (h.length ? '<button class="btn ghost small" id="clearHist">Clear</button>' : '') + '</div>'
      + '<p class="muted">Your last 50 requests, kept in this browser. Click one to open it again with the same values.</p>'
      + '<div class="panel">' + (rows || '<div class="empty">Nothing sent yet.</div>') + '</div>';
    document.querySelectorAll('.history-item').forEach(function (el) {
      var open = function () { reopen(h[+el.dataset.i]); };
      el.addEventListener('click', open);
      el.addEventListener('keydown', function (e) { if (e.key === 'Enter') open(); });
    });
    var c = $('#clearHist');
    if (c) c.addEventListener('click', function () { store.set('history', []); showHistory(); });
  }

  function reopen(x) {
    var o = opsById[x.opId];
    if (!o) return;
    location.hash = '#/op/' + o.id;
    setTimeout(function () {
      // Path params back into their pickers.
      var tpl = o.path.split('?')[0].split('/'), got = x.path.split('?')[0].split('/');
      tpl.forEach(function (seg, i) {
        var m = seg.match(/^\{(.+)\}$/);
        if (m) setParam(m[1], decodeURIComponent(got[i] || ''));
      });
      var q = x.path.split('?')[1];
      if (q) q.split('&').forEach(function (kv) { var p = kv.split('='); setParam(decodeURIComponent(p[0]), decodeURIComponent(p[1] || '')); });
      var ta = $('#body');
      if (ta && x.body != null) { ta.value = x.body; current.bodyTouched = true; checkJSON(); }
      updateUrlPreview();
    }, 60);
  }

  function setParam(name, value) {
    var el = $('[data-param="' + name + '"]');
    if (!el) return;
    if (el.tagName === 'SELECT') {
      if (!Array.prototype.some.call(el.options, function (op) { return op.value === value; })) {
        el.value = '__custom';
        var c = $('[data-custom-for="' + name + '"]');
        c.hidden = false; c.value = value;
      } else {
        el.value = value;
      }
    } else {
      el.value = value;
    }
  }

  // ───────────────────────── models ─────────────────────────

  function showModels(name) {
    current = null;
    var all = doc.components.schemas;
    var names = Object.keys(all).sort();
    $('#main').innerHTML = '<h1>Models</h1><p class="muted">Every object the API sends or accepts. Fields marked <span class="req">*</span> are required in requests.</p>'
      + names.map(function (n) {
        return '<section class="panel" id="model-' + esc(n) + '" style="margin-top:14px"><div class="panel-head">' + esc(n) + '</div><div class="panel-body">'
          + '<p class="muted">' + esc(all[n].description || '') + '</p>' + fieldsTable({ $ref: '#/components/schemas/' + n }) + '</div></section>';
      }).join('');
    if (name) { var el = document.getElementById('model-' + name); if (el) el.scrollIntoView(); }
  }

  // ───────────────────────── small things ─────────────────────────

  function toast(msg) {
    var t = document.createElement('div');
    t.className = 'toast'; t.setAttribute('role', 'status'); t.textContent = msg;
    document.body.appendChild(t);
    setTimeout(function () { t.remove(); }, 2200);
  }

  function copy(text) {
    (navigator.clipboard ? navigator.clipboard.writeText(text) : Promise.reject()).then(function () { toast('Copied'); }, function () {
      var ta = document.createElement('textarea'); ta.value = text; document.body.appendChild(ta); ta.select();
      try { document.execCommand('copy'); toast('Copied'); } catch (e) { toast('Copy failed'); }
      ta.remove();
    });
  }

  function setTokenLabel() {
    $('#tokenBtn').innerHTML = '🔑 <span class="tok-label">' + (token() ? 'Token set' : 'Token') + '</span>';
  }

  function wireChrome() {
    $('#search').addEventListener('input', function (e) { buildNav(e.target.value); });
    $('#search').addEventListener('keydown', function (e) {
      if (e.key === 'Enter') { var first = $('#nav .nav-item[href^="#/op/"]'); if (first) location.hash = first.getAttribute('href'); }
    });
    document.addEventListener('keydown', function (e) {
      if (e.key === '/' && !/INPUT|TEXTAREA|SELECT/.test(document.activeElement.tagName)) { e.preventDefault(); $('#search').focus(); }
      if ((e.ctrlKey || e.metaKey) && e.key === 'Enter' && current) { e.preventDefault(); send(); }
    });
    $('#menuBtn').addEventListener('click', function () {
      var open = $('#side').classList.toggle('open');
      $('#menuBtn').setAttribute('aria-expanded', open ? 'true' : 'false');
    });
    $('#themeBtn').addEventListener('click', function () {
      var dark = document.documentElement.dataset.theme
        ? document.documentElement.dataset.theme === 'dark'
        : matchMedia('(prefers-color-scheme: dark)').matches;
      var next = dark ? 'light' : 'dark';
      document.documentElement.dataset.theme = next;
      try { localStorage.setItem('fpvapi.theme', next); } catch (e) {}
    });
    var dlg = $('#tokenDialog');
    $('#tokenBtn').addEventListener('click', function () { $('#tokenInput').value = token(); dlg.showModal(); });
    $('#tokenShow').addEventListener('change', function (e) { $('#tokenInput').type = e.target.checked ? 'text' : 'password'; });
    dlg.addEventListener('close', function () {
      if (dlg.returnValue === 'save') store.set('token', $('#tokenInput').value.trim());
      if (dlg.returnValue === 'clear') store.set('token', '');
      setTokenLabel();
      pollStatus();
      if (current) renderCode();
    });
    setTokenLabel();
    window.addEventListener('hashchange', route);
  }

  // ───────────────────────── start ─────────────────────────

  getJSON(API + '/openapi.json').then(function (d) {
    doc = d;
    Object.keys(doc.paths).forEach(function (path) {
      Object.keys(doc.paths[path]).forEach(function (m) {
        var op = doc.paths[path][m];
        var o = { id: op.operationId, method: m.toUpperCase(), path: path, op: op, tag: op.tags[0] };
        ops.push(o);
        opsById[o.id] = o;
      });
    });
    wireChrome();
    buildNav('');
    return refreshLists();
  }).then(function () {
    route();
    pollStatus();
    setInterval(function () { if (!document.hidden) pollStatus(); }, 1500);
  }).catch(function (e) {
    $('#main').innerHTML = '<h1>Can\'t load the API description</h1><p class="muted">' + esc(e.message) + '</p>'
      + '<p>Make sure FPVTrackside is running with its web server switched on, then reload.</p>';
  });
})();
