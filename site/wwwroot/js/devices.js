(() => {
    const roots = Array.from(document.querySelectorAll('[data-status-url]'));
    const snmpPanel = document.querySelector('[data-snmp-url]');

    if (roots.length === 0 && !snmpPanel) return;

    const endpoint = roots.length > 0 ? roots[0].dataset.statusUrl : null;
    const snmpEndpoint = snmpPanel ? snmpPanel.dataset.snmpUrl : null;
    const snmpTestEndpoint = snmpPanel?.dataset.snmpTestUrl;
    const deviceId = document.querySelector('.device-summary-card')?.dataset.deviceId || 'unknown';
    const hiddenInterfacesKey = `snmp-hidden-interfaces:${deviceId}`;
    const readHiddenInterfaces = () => {
        try { return new Set(JSON.parse(localStorage.getItem(hiddenInterfacesKey) || '[]').map(Number)); }
        catch { return new Set(); }
    };
    const hiddenInterfaceIndexes = readHiddenInterfaces();
    let showHiddenInterfaces = document.getElementById('show-hidden-interfaces');
    const escapeHtml = value => String(value ?? '').replace(/[&<>"']/g, character => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[character]);

    const applyInterfaceVisibility = row => {
        const index = Number(row.dataset.interfaceIndex);
        const hidden = hiddenInterfaceIndexes.has(index);
        row.dataset.hidden = String(hidden);
        row.hidden = hidden && !showHiddenInterfaces?.checked;
        const button = row.querySelector('[data-interface-toggle]');
        if (button) button.textContent = hidden ? 'Show' : 'Hide';
    };
    const saveInterfaceVisibility = () => {
        try { localStorage.setItem(hiddenInterfacesKey, JSON.stringify([...hiddenInterfaceIndexes])); } catch { /* Keep this view usable when browser storage is unavailable. */ }
        document.querySelectorAll('#interfaces-tbody tr[data-interface-index]').forEach(applyInterfaceVisibility);
    };

    showHiddenInterfaces?.addEventListener('change', () => {
        document.querySelectorAll('#interfaces-tbody tr[data-interface-index]').forEach(applyInterfaceVisibility);
    });
    document.getElementById('interfaces-container')?.addEventListener('click', event => {
        const button = event.target.closest('[data-interface-toggle]');
        if (!button) return;
        const index = Number(button.dataset.interfaceToggle);
        if (!Number.isInteger(index) || index < 1) return;
        if (hiddenInterfaceIndexes.has(index)) hiddenInterfaceIndexes.delete(index);
        else hiddenInterfaceIndexes.add(index);
        saveInterfaceVisibility();
    });

    const formatDate = value => {
        if (!value) return '—';
        const date = new Date(value);
        return Number.isNaN(date.valueOf()) ? '—' : new Intl.DateTimeFormat(undefined, {
            month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit', second: '2-digit'
        }).format(date);
    };

    const formatDuration = since => {
        const started = Date.parse(since || '');
        if (Number.isNaN(started)) return 'Not checked';
        let seconds = Math.max(0, Math.floor((Date.now() - started) / 1000));
        const days = Math.floor(seconds / 86400); seconds %= 86400;
        const hours = Math.floor(seconds / 3600); seconds %= 3600;
        const minutes = Math.floor(seconds / 60); seconds %= 60;
        const clock = [hours, minutes, seconds].map(value => String(value).padStart(2, '0')).join(':');
        return `${days ? `${days}d ` : ''}${clock}`;
    };

    const formatBitsRate = bps => {
        if (bps == null) return null;
        if (bps >= 1_000_000_000) return `${(bps / 1_000_000_000).toFixed(2)} Gbps`;
        if (bps >= 1_000_000) return `${(bps / 1_000_000).toFixed(2)} Mbps`;
        if (bps >= 1_000) return `${(bps / 1_000).toFixed(2)} Kbps`;
        return `${bps.toFixed(1)} bps`;
    };

    const formatUptime = hundredths => {
        if (hundredths == null) return null;
        let totalSeconds = Math.floor(hundredths / 100);
        const days = Math.floor(totalSeconds / 86400); totalSeconds %= 86400;
        const hours = Math.floor(totalSeconds / 3600); totalSeconds %= 3600;
        const minutes = Math.floor(totalSeconds / 60); totalSeconds %= 60;
        const clock = [hours, minutes, totalSeconds].map(v => String(v).padStart(2, '0')).join(':');
        return `${days ? `${days}.` : ''}${clock}`;
    };

    const getOperStatusInfo = state => {
        switch (state) {
            case 1: return { statusClass: 'status-up', text: 'Up' };
            case 2: return { statusClass: 'status-down', text: 'Down' };
            case 3: return { statusClass: 'status-testing', text: 'Testing' };
            case 5: return { statusClass: 'status-dormant', text: 'Dormant' };
            case 6: return { statusClass: 'status-unknown', text: 'Not present' };
            case 7: return { statusClass: 'status-down', text: 'Lower down' };
            default: return { statusClass: 'status-unknown', text: state != null ? `State ${state}` : 'Not supported' };
        }
    };

    const updateDuration = element => {
        const status = element.dataset.status;
        const enabled = element.dataset.monitoringEnabled !== 'false';
        const target = element.querySelector('[data-field="duration"]');
        if (!target) return;
        if (!enabled) {
            target.textContent = 'Monitoring paused';
        } else if (status === 'Unknown') {
            target.textContent = 'Not checked';
        } else {
            target.textContent = `${status} for ${formatDuration(element.dataset.stateSince)}`;
        }
    };

    const updateRecord = (element, result) => {
        element.dataset.status = result.status;
        element.dataset.stateSince = result.currentStateSinceUtc || '';
        element.dataset.monitoringEnabled = result.monitoringEnabled ? 'true' : 'false';
        const badge = element.querySelector('.device-status');
        if (badge) {
            badge.className = `device-status status-${result.status.toLowerCase()}`;
            const label = badge.querySelector('[data-field="status"]');
            if (label) label.textContent = result.status;
        }
        const response = element.querySelector('[data-field="response"]');
        if (response) response.textContent = result.responseTimeMilliseconds == null ? '—' : `${result.responseTimeMilliseconds} ms`;
        const lastCheck = element.querySelector('[data-field="last-check"]');
        if (lastCheck) lastCheck.textContent = formatDate(result.lastCheckUtc);
        const lastSuccess = element.querySelector('[data-field="last-success"]');
        if (lastSuccess) lastSuccess.textContent = formatDate(result.lastSuccessfulCheckUtc);
        const lastFailure = element.querySelector('[data-field="last-failure"]');
        if (lastFailure) lastFailure.textContent = formatDate(result.lastFailedCheckUtc);
        const monitoring = element.querySelector('[data-field="monitoring"]');
        if (monitoring) monitoring.textContent = result.monitoringEnabled ? 'Enabled' : 'Paused';
        updateDuration(element);
    };

    const refreshGeneral = async () => {
        if (!endpoint) return;
        try {
            const response = await fetch(endpoint, { headers: { 'Accept': 'application/json' }, cache: 'no-store' });
            if (!response.ok) return;
            const devices = await response.json();
            const byId = new Map(devices.map(device => [device.id.toLowerCase(), device]));
            for (const element of document.querySelectorAll('[data-device-id]')) {
                const result = byId.get(element.dataset.deviceId.toLowerCase());
                if (result) updateRecord(element, result);
            }
            for (const counter of document.querySelectorAll('[data-status-count]')) {
                counter.textContent = String(devices.filter(device => device.status === counter.dataset.statusCount).length);
            }
            for (const counter of document.querySelectorAll('[data-inventory-total]')) {
                counter.textContent = String(devices.length);
            }
            for (const segment of document.querySelectorAll('[data-status-share]')) {
                const count = devices.filter(device => device.status === segment.dataset.statusShare).length;
                segment.style.width = devices.length === 0 ? '0%' : `${count / devices.length * 100}%`;
            }
        } catch {
            // A transient refresh failure leaves the last successfully rendered state visible.
        }
    };

    const flashElement = el => {
        if (!el) return;
        el.classList.remove('flash-updated');
        void el.offsetWidth; // trigger reflow
        el.classList.add('flash-updated');
    };

    const refreshSnmp = async () => {
        if (!snmpEndpoint) return;
        try {
            const response = await fetch(snmpEndpoint, { headers: { 'Accept': 'application/json' }, cache: 'no-store' });
            if (!response.ok) return;
            const data = await response.json();

            // Also update parent device summary if present
            const deviceCard = document.querySelector('.device-summary-card');
            if (deviceCard) {
                updateRecord(deviceCard, data);
            }

            if (!data.snmp) return;
            const snmp = data.snmp;

            // Update timestamp
            const collectedEl = snmpPanel.querySelector('[data-field="snmp-collected"]');
            if (collectedEl && snmp.collectedUtc) {
                collectedEl.textContent = formatDate(snmp.collectedUtc);
            }

            // Update CPU & Memory & Uptime
            const cpuEl = snmpPanel.querySelector('[data-field="snmp-cpu"]');
            if (cpuEl) cpuEl.textContent = snmp.cpuUtilizationPercent != null ? `${snmp.cpuUtilizationPercent.toFixed(1)}%` : 'Not supported';

            const memEl = snmpPanel.querySelector('[data-field="snmp-memory"]');
            if (memEl) memEl.textContent = snmp.memoryUtilizationPercent != null ? `${snmp.memoryUtilizationPercent.toFixed(1)}%` : 'Not supported';

            const uptimeEl = snmpPanel.querySelector('[data-field="snmp-uptime"]');
            if (uptimeEl) {
                const formattedUptime = formatUptime(snmp.systemUptimeHundredths);
                uptimeEl.textContent = formattedUptime || 'Not supported';
            }

            const descEl = snmpPanel.querySelector('[data-field="snmp-description"]');
            if (descEl && snmp.systemDescription) descEl.textContent = snmp.systemDescription;

            const nameEl = snmpPanel.querySelector('[data-field="snmp-name"]');
            if (nameEl && snmp.systemName) nameEl.textContent = snmp.systemName;

            // Update error notice
            const errorContainer = document.getElementById('snmp-error-container');
            if (errorContainer) {
                if (snmp.collectionError) {
                    const notice = document.createElement('div');
                    notice.className = 'notice notice-warning';
                    notice.textContent = snmp.collectionError;
                    errorContainer.replaceChildren(notice);
                } else {
                    errorContainer.replaceChildren();
                }
            }

            // Update Interfaces Table
            if (Array.isArray(snmp.interfaces) && snmp.interfaces.length > 0) {
                let tbody = document.getElementById('interfaces-tbody');
                if (!tbody) {
                    const container = document.getElementById('interfaces-container');
                    container.replaceChildren();
                    const toolbar = document.createElement('div');
                    toolbar.className = 'snmp-interface-toolbar';
                    const label = document.createElement('label');
                    const checkbox = document.createElement('input');
                    checkbox.type = 'checkbox'; checkbox.id = 'show-hidden-interfaces';
                    label.append(checkbox, document.createTextNode(' Show hidden interfaces'));
                    const note = document.createElement('span');
                    note.textContent = 'VLAN is read from Cisco port membership or the standard bridge PVID table when exposed by the device.';
                    toolbar.append(label, note);
                    const responsive = document.createElement('div');
                    responsive.className = 'table-responsive';
                    const table = document.createElement('table');
                    table.className = 'device-table'; table.id = 'interfaces-table'; table.setAttribute('aria-label', 'SNMP interface metrics');
                    table.innerHTML = '<thead><tr><th>Interface</th><th>Port status</th><th>Assigned VLAN</th><th>Speed</th><th>In traffic (Live)</th><th>Out traffic (Live)</th><th>Received bytes</th><th>Sent bytes</th><th>Visibility</th></tr></thead><tbody id="interfaces-tbody"></tbody>';
                    responsive.append(table); container.append(toolbar, responsive);
                    tbody = document.getElementById('interfaces-tbody');
                    const newToggle = document.getElementById('show-hidden-interfaces');
                    showHiddenInterfaces = newToggle;
                    newToggle.checked = false;
                    newToggle.addEventListener('change', () => document.querySelectorAll('#interfaces-tbody tr[data-interface-index]').forEach(applyInterfaceVisibility));
                }
                if (tbody) {
                    for (const iface of snmp.interfaces) {
                        let row = tbody.querySelector(`tr[data-interface-index="${iface.index}"]`);
                        const stInfo = getOperStatusInfo(iface.operationalStatus);
                        const inRateFormatted = formatBitsRate(iface.inBitsPerSecond);
                        const outRateFormatted = formatBitsRate(iface.outBitsPerSecond);
                        const inTrafficHtml = inRateFormatted
                            ? `<div class="traffic-cell in-traffic"><span class="traffic-icon">↓</span><span>${inRateFormatted}</span></div>`
                            : '<span class="traffic-muted">Waiting samples</span>';
                        const outTrafficHtml = outRateFormatted
                            ? `<div class="traffic-cell out-traffic"><span class="traffic-icon">↑</span><span>${outRateFormatted}</span></div>`
                            : '<span class="traffic-muted">Waiting samples</span>';
                        const speedFormatted = iface.speedBitsPerSecond != null ? `${(iface.speedBitsPerSecond / 1_000_000).toFixed(1)} Mbps` : 'Not supported';
                        const inBytesFormatted = iface.inOctets != null ? Number(iface.inOctets).toLocaleString() : 'Not supported';
                        const outBytesFormatted = iface.outOctets != null ? Number(iface.outOctets).toLocaleString() : 'Not supported';

                        if (row) {
                            // Update existing row
                            const statusCell = row.querySelector('[data-field="port-status"]');
                            if (statusCell) {
                                statusCell.innerHTML = `<span class="device-status ${stInfo.statusClass}"><span class="status-dot"></span><span data-field="port-status-text">${stInfo.text}</span></span>`;
                                let admin = statusCell.querySelector('[data-field="admin-status"]');
                                if (!admin) { admin = document.createElement('small'); admin.dataset.field = 'admin-status'; statusCell.append(admin); }
                                admin.textContent = `Admin: ${iface.adminStatus === 1 ? 'Up' : iface.adminStatus === 2 ? 'Down' : 'Not exposed'}`;
                            }
                            const speedCell = row.querySelector('[data-field="port-speed"]');
                            if (speedCell) speedCell.textContent = speedFormatted;
                            const vlanCell = row.querySelector('[data-field="port-vlan"]');
                            if (vlanCell) vlanCell.textContent = iface.vlanId != null ? `VLAN ${iface.vlanId}` : 'Not exposed';

                            const inTrafficCell = row.querySelector('[data-field="port-in-traffic"]');
                            if (inTrafficCell) inTrafficCell.innerHTML = inTrafficHtml;

                            const outTrafficCell = row.querySelector('[data-field="port-out-traffic"]');
                            if (outTrafficCell) outTrafficCell.innerHTML = outTrafficHtml;

                            const inBytesCell = row.querySelector('[data-field="port-in-bytes"]');
                            if (inBytesCell) inBytesCell.textContent = inBytesFormatted;

                            const outBytesCell = row.querySelector('[data-field="port-out-bytes"]');
                            if (outBytesCell) outBytesCell.textContent = outBytesFormatted;

                            applyInterfaceVisibility(row);
                            flashElement(row);
                        } else {
                            // Row doesn't exist yet, insert it
                            const newRow = document.createElement('tr');
                            newRow.dataset.interfaceIndex = iface.index;
                            newRow.innerHTML = `
                                <td><strong>${escapeHtml(iface.name || ('Interface ' + iface.index))}</strong><span class="device-ip">ifIndex ${iface.index}${iface.description && iface.description !== iface.name ? ' · ' + escapeHtml(iface.description) : ''}</span></td>
                                <td data-field="port-status"><span class="device-status ${stInfo.statusClass}"><span class="status-dot"></span><span data-field="port-status-text">${stInfo.text}</span></span><small data-field="admin-status">Admin: ${iface.adminStatus === 1 ? 'Up' : iface.adminStatus === 2 ? 'Down' : 'Not exposed'}</small></td>
                                <td data-field="port-vlan">${iface.vlanId != null ? `VLAN ${iface.vlanId}` : 'Not exposed'}</td>
                                <td data-field="port-speed">${speedFormatted}</td>
                                <td data-field="port-in-traffic">${inTrafficHtml}</td>
                                <td data-field="port-out-traffic">${outTrafficHtml}</td>
                                <td data-field="port-in-bytes">${inBytesFormatted}</td>
                                <td data-field="port-out-bytes">${outBytesFormatted}</td>
                                <td><button type="button" class="app-secondary-button interface-visibility-toggle" data-interface-toggle="${iface.index}">Hide</button></td>
                            `;
                            tbody.appendChild(newRow);
                            applyInterfaceVisibility(newRow);
                            flashElement(newRow);
                        }
                    }
                }
            }
        } catch {
            // Suppress network jitter errors
        }
    };

    // Initialize state
    for (const element of document.querySelectorAll('[data-device-id]')) updateDuration(element);
    document.querySelectorAll('#interfaces-tbody tr[data-interface-index]').forEach(applyInterfaceVisibility);
    
    // Initial fetches
    refreshGeneral();
    if (snmpEndpoint) refreshSnmp();

    document.getElementById('test-snmp-now')?.addEventListener('click', async event => {
        const button = event.currentTarget;
        const result = document.getElementById('snmp-test-result');
        const token = document.querySelector('#snmp-test-antiforgery input[name="__RequestVerificationToken"]')?.value || '';
        button.disabled = true;
        button.textContent = 'Testing…';
        result.hidden = false;
        result.className = 'snmp-test-result is-info';
        result.textContent = 'Sending a live SNMP request to the device…';
        try {
            const response = await fetch(snmpTestEndpoint, {
                method: 'POST', credentials: 'same-origin', cache: 'no-store',
                headers: { 'Accept': 'application/json', 'RequestVerificationToken': token }
            });
            const data = await response.json().catch(() => ({}));
            if (!response.ok) throw new Error(data.error || data.title || `Request failed (${response.status}).`);
            result.className = `snmp-test-result ${data.success ? 'is-success' : 'is-error'}`;
            result.textContent = data.message;
            await refreshSnmp();
        } catch (error) {
            result.className = 'snmp-test-result is-error';
            result.textContent = error.message || 'SNMP test failed.';
        } finally {
            button.disabled = false;
            button.textContent = 'Test SNMP now';
        }
    });

    // Periodic intervals
    window.setInterval(refreshGeneral, 10000);
    if (snmpEndpoint) {
        window.setInterval(refreshSnmp, 10000);
    }

    // Per-second clock tick for duration counter
    window.setInterval(() => {
        for (const element of document.querySelectorAll('[data-device-id]')) updateDuration(element);
    }, 1000);
})();
