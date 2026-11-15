(() => {
    const root = document.getElementById('live-ping');
    if (!root) return;

    const form = document.getElementById('live-ping-form');
    const targetInput = document.getElementById('live-ping-target');
    const timeoutInput = document.getElementById('live-ping-timeout');
    const rows = document.getElementById('live-ping-rows');
    const empty = document.getElementById('live-ping-empty');
    const count = document.getElementById('live-ping-count');
    const state = document.getElementById('live-ping-state');
    const stateLabel = document.getElementById('live-ping-state-label');
    const message = document.getElementById('live-ping-message');
    const sessions = new Map();
    let pollTimer = null;
    let polling = false;

    const showMessage = (text, error = false) => {
        message.textContent = text;
        message.className = `discovery-message is-visible${error ? ' is-error' : ''}`;
        message.hidden = !text;
    };

    const updateHeader = () => {
        const all = [...sessions.values()];
        const running = all.filter(item => item.running).length;
        count.textContent = `${all.length} target${all.length === 1 ? '' : 's'}`;
        empty.hidden = all.length > 0;
        state.className = `live-ping-indicator ${running ? (polling ? 'is-polling' : 'is-live') : 'is-idle'}`;
        stateLabel.textContent = running ? (polling ? 'Checking' : 'Live') : 'Idle';
    };

    const cell = (text, className) => {
        const td = document.createElement('td');
        td.textContent = text;
        if (className) td.className = className;
        return td;
    };

    const render = () => {
        rows.replaceChildren();
        for (const item of sessions.values()) {
            const row = document.createElement('tr');
            row.className = item.lastReachable === false || item.losses > 0 ? 'live-ping-row-down' : item.lastReachable === true ? 'live-ping-row-up' : '';
            row.dataset.targetKey = item.key;
            row.append(cell(item.target, 'live-ping-target-cell'));
            row.append(cell(item.resolvedAddress || '—'));
            row.append(cell(item.running ? (item.status || 'Waiting for first reply') : (item.status || 'Paused'), item.lastReachable === false ? 'live-ping-status-down' : item.lastReachable === true ? 'live-ping-status-up' : ''));
            row.append(cell(item.latency == null ? '—' : `${item.latency} ms`));
            row.append(cell(item.ttl == null ? '—' : String(item.ttl)));
            row.append(cell(item.replyBytes == null ? '—' : String(item.replyBytes)));
            const lost = item.sent ? `${item.losses}/${item.sent} lost (${Math.round(item.losses * 100 / item.sent)}%)` : '0/0';
            row.append(cell(lost, item.losses > 0 ? 'live-ping-loss' : ''));
            row.append(cell(item.lastReplyAt ? item.lastReplyAt.toLocaleTimeString() : '—'));

            const action = document.createElement('td');
            action.className = 'live-ping-actions';
            const toggle = document.createElement('button');
            toggle.type = 'button';
            toggle.className = 'app-secondary-button';
            toggle.dataset.action = 'toggle';
            toggle.dataset.key = item.key;
            toggle.textContent = item.running ? 'Pause' : 'Resume';
            action.append(toggle);
            const remove = document.createElement('button');
            remove.type = 'button';
            remove.className = 'app-secondary-button live-ping-remove';
            remove.dataset.action = 'remove';
            remove.dataset.key = item.key;
            remove.textContent = 'Remove';
            action.append(remove);
            row.append(action);
            rows.append(row);
        }
        updateHeader();
    };

    const schedulePoll = delay => {
        window.clearTimeout(pollTimer);
        if ([...sessions.values()].some(item => item.running)) {
            pollTimer = window.setTimeout(poll, delay);
        }
    };

    const poll = async () => {
        const active = [...sessions.values()].filter(item => item.running);
        if (!active.length || polling) return;
        polling = true;
        updateHeader();
        try {
            const token = document.querySelector('#live-ping-antiforgery input[name="__RequestVerificationToken"]')?.value || '';
            const response = await fetch(root.dataset.probeUrl, {
                method: 'POST',
                credentials: 'same-origin',
                cache: 'no-store',
                headers: { 'Content-Type': 'application/json', 'Accept': 'application/json', 'RequestVerificationToken': token },
                body: JSON.stringify({ targets: active.map(item => item.target), timeoutMilliseconds: Number(timeoutInput.value) })
            });
            const data = await response.json().catch(() => ({}));
            if (!response.ok) throw new Error(data.error || data.title || `Ping request failed (${response.status}).`);
            for (const result of data.results || []) {
                const item = sessions.get(String(result.target || '').toLocaleLowerCase());
                if (!item || !item.running) continue;
                item.sent++;
                item.status = result.status || (result.isReachable ? 'Success' : 'Unknown');
                item.resolvedAddress = result.resolvedAddress || item.resolvedAddress;
                item.lastReachable = !!result.isReachable;
                item.latency = result.responseTimeMilliseconds;
                item.ttl = result.timeToLive;
                item.replyBytes = result.replyBytes;
                item.lastReplyAt = new Date();
                if (item.lastReachable) item.received++;
                else item.losses++;
                item.error = result.error || null;
            }
            showMessage('Latest ICMP replies received from the application server.');
        } catch (error) {
            for (const item of active) {
                if (!item.running) continue;
                item.sent++;
                item.losses++;
                item.lastReachable = false;
                item.status = 'ProbeError';
                item.latency = null;
                item.ttl = null;
                item.replyBytes = null;
                item.lastReplyAt = new Date();
            }
            showMessage(error.message || 'Ping request failed.', true);
        } finally {
            polling = false;
            render();
            schedulePoll(Math.max(300, Number(timeoutInput.value)));
        }
    };

    form.addEventListener('submit', event => {
        event.preventDefault();
        const target = targetInput.value.trim();
        if (!target) return;
        const key = target.toLocaleLowerCase();
        if (sessions.has(key)) {
            const existing = sessions.get(key);
            existing.running = true;
            showMessage(`${target} is already listed. Its ping has resumed.`);
        } else if (sessions.size >= 24) {
            showMessage('A maximum of 24 targets can be pinged at once.', true);
            return;
        } else {
            sessions.set(key, { key, target, running: true, sent: 0, received: 0, losses: 0, status: null, resolvedAddress: null, latency: null, ttl: null, replyBytes: null, lastReachable: null, lastReplyAt: null });
            showMessage(`Started live ping for ${target}.`);
        }
        targetInput.value = '';
        render();
        schedulePoll(0);
        targetInput.focus();
    });

    rows.addEventListener('click', event => {
        const button = event.target.closest('button[data-action]');
        if (!button) return;
        const item = sessions.get(button.dataset.key);
        if (!item) return;
        if (button.dataset.action === 'remove') sessions.delete(item.key);
        else item.running = !item.running;
        render();
        schedulePoll(0);
    });

    render();
})();
