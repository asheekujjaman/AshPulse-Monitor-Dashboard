(() => {
    const root = document.getElementById('ipam-discovery');
    if (!root) return;
    const $ = id => document.getElementById(id);
    const form = $('ipam-discovery-scan');
    const scanButton = $('ipam-discovery-scan-button');
    const subnet = $('ipam-discovery-subnet');
    const timeout = $('ipam-discovery-timeout');
    const results = $('ipam-discovery-results');
    const rows = $('ipam-discovery-rows');
    const message = $('ipam-discovery-message');
    const selectAll = $('ipam-discovery-select-all');
    const addButton = $('ipam-discovery-add');
    let discovered = [];
    let selected = new Set();
    let singleAddIp = null;

    const showMessage = (text, error = false) => {
        message.textContent = text;
        message.className = `discovery-message is-visible${error ? ' is-error' : ''}`;
        message.hidden = !text;
    };
    const request = async (url, data) => {
        const token = $('ipam-discovery-antiforgery').querySelector('input[name="__RequestVerificationToken"]')?.value || '';
        const response = await fetch(url, { method: 'POST', credentials: 'same-origin', cache: 'no-store', headers: { 'Content-Type': 'application/json', Accept: 'application/json', RequestVerificationToken: token }, body: JSON.stringify(data) });
        const body = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(body.error || body.title || `Request failed (${response.status}).`);
        return body;
    };
    const setSelected = () => {
        const selectable = discovered.filter(item => !item.alreadyInInventory);
        $('ipam-discovery-count').textContent = singleAddIp ? `Adding ${singleAddIp}` : `${selected.size} selected`;
        addButton.disabled = selected.size === 0;
        addButton.textContent = singleAddIp ? `Add ${singleAddIp} to IPAM` : 'Add selected to IPAM';
        $('ipam-discovery-details-heading').textContent = singleAddIp ? `Details for ${singleAddIp}` : 'Details for selected IPs';
        $('ipam-discovery-details-help').textContent = singleAddIp
            ? 'Add any extra inventory details for this address, then save it to IPAM. Existing monitored device details are linked automatically.'
            : 'Shared details apply to every selected record. Existing monitored devices are linked automatically.';
        selectAll.disabled = selectable.length === 0;
        selectAll.checked = selectable.length > 0 && selectable.every(item => selected.has(item.ipAddress));
        selectAll.indeterminate = selected.size > 0 && !selectAll.checked;
    };
    const render = () => {
        rows.replaceChildren();
        discovered.forEach(item => {
            const row = document.createElement('tr');
            const selectCell = document.createElement('td');
            const checkbox = document.createElement('input');
            checkbox.type = 'checkbox'; checkbox.className = 'discovery-row-select'; checkbox.dataset.ip = item.ipAddress;
            checkbox.checked = selected.has(item.ipAddress); checkbox.disabled = item.alreadyInInventory;
            checkbox.setAttribute('aria-label', `Select ${item.ipAddress}`); selectCell.append(checkbox); row.append(selectCell);
            const ip = document.createElement('td'); const code = document.createElement('code'); code.textContent = item.ipAddress; ip.append(code); row.append(ip);
            const nameCell = document.createElement('td'); const name = document.createElement('input');
            name.className = 'form-control'; name.maxLength = 120; name.value = item.suggestedName || `Device-${item.ipAddress}`; name.dataset.nameIp = item.ipAddress;
            name.disabled = item.alreadyInInventory; name.setAttribute('aria-label', `Device name for ${item.ipAddress}`); nameCell.append(name); row.append(nameCell);
            row.insertAdjacentHTML('beforeend', `<td></td><td></td><td></td><td></td>`);
            row.children[3].textContent = item.suggestedType || 'Other';
            row.children[4].textContent = `${item.responseTimeMilliseconds ?? 0} ms`;
            const status = document.createElement('span'); status.className = `device-status ${item.alreadyInInventory ? 'status-unknown' : 'status-up'}`; status.textContent = item.alreadyInInventory ? 'IN IPAM' : 'NEW'; row.children[5].append(status);
            const actionCell = row.children[6];
            if (item.alreadyInInventory && item.existingIpamRecordId) {
                const update = document.createElement('a'); update.className = 'app-secondary-button'; update.textContent = 'Update';
                const editUrl = new URL(root.dataset.editUrl, window.location.origin); editUrl.searchParams.set('id', item.existingIpamRecordId); update.href = editUrl.pathname + editUrl.search;
                update.setAttribute('aria-label', `Update IPAM record for ${item.ipAddress}`); actionCell.append(update);
            } else if (!item.alreadyInInventory) {
                const add = document.createElement('button'); add.type = 'button'; add.className = 'app-secondary-button ipam-discovery-add-one'; add.textContent = 'Add';
                add.dataset.addIp = item.ipAddress; add.setAttribute('aria-label', `Add ${item.ipAddress} to IPAM`); actionCell.append(add);
            }
            rows.append(row);
        });
        $('ipam-discovery-summary').textContent = `${discovered.length} responsive host(s) in ${subnet.value}. IPAM records already present are marked and cannot be selected.`;
        results.hidden = false;
        setSelected();
    };
    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (!subnet.value) return;
        scanButton.disabled = true; scanButton.textContent = 'Scanning…'; selected.clear();
        showMessage('Scanning subnet. This may take a few moments.');
        try {
            const data = await request(root.dataset.scanUrl, { cidr: subnet.value, timeoutMilliseconds: Number(timeout.value) });
            if (!data.success) throw new Error(data.error || 'Subnet scan failed.');
            discovered = data.devices || []; singleAddIp = null; render();
            showMessage(`Scanned ${data.totalScanned} addresses; ${data.responsiveCount} responded.`);
        } catch (error) { showMessage(error.message, true); }
        finally { scanButton.disabled = false; scanButton.textContent = 'Scan subnet'; }
    });
    rows.addEventListener('change', event => {
        const box = event.target.closest('input[type="checkbox"][data-ip]');
        if (!box) return;
        singleAddIp = null;
        if (box.checked) selected.add(box.dataset.ip); else selected.delete(box.dataset.ip);
        setSelected();
    });
    rows.addEventListener('click', event => {
        const button = event.target.closest('button[data-add-ip]');
        if (!button) return;
        const ip = button.dataset.addIp;
        const item = discovered.find(device => device.ipAddress === ip && !device.alreadyInInventory);
        if (!item) return;
        singleAddIp = ip;
        selected.clear(); selected.add(ip);
        $('ipam-discovery-type').value = item.suggestedType || 'Other';
        render();
        $('ipam-discovery-details').scrollIntoView({ behavior: 'smooth', block: 'center' });
        $('ipam-discovery-type').focus({ preventScroll: true });
        showMessage(`Add details for ${ip}, then save it to IPAM.`);
    });
    selectAll.addEventListener('change', () => {
        singleAddIp = null;
        discovered.filter(item => !item.alreadyInInventory).forEach(item => selectAll.checked ? selected.add(item.ipAddress) : selected.delete(item.ipAddress));
        render();
    });
    addButton.addEventListener('click', async () => {
        const picks = discovered.filter(item => selected.has(item.ipAddress));
        if (!picks.length) return;
        addButton.disabled = true; addButton.textContent = 'Saving…';
        try {
            const details = {
                subnetCidr: subnet.value,
                deviceName: singleAddIp ? rows.querySelector(`[data-name-ip="${CSS.escape(singleAddIp)}"]`)?.value.trim() || `Device-${singleAddIp}` : null,
                deviceType: $('ipam-discovery-type').value.trim() || null,
                assignmentStatus: Number($('ipam-discovery-status').value),
                vlanId: $('ipam-discovery-vlan').value ? Number($('ipam-discovery-vlan').value) : null,
                vlanName: $('ipam-discovery-vlan-name').value.trim() || null,
                locationId: $('ipam-discovery-location').value || null,
                buildingId: $('ipam-discovery-building').value || null,
                groupId: $('ipam-discovery-group').value || null,
                switchName: $('ipam-discovery-switch').value.trim() || null,
                switchIpAddress: $('ipam-discovery-switch-ip').value.trim() || null,
                switchPort: $('ipam-discovery-port').value.trim() || null,
                macAddress: $('ipam-discovery-mac').value.trim() || null,
                assignedTo: $('ipam-discovery-assigned-to').value.trim() || null,
                department: $('ipam-discovery-department').value.trim() || null,
                description: $('ipam-discovery-description').value.trim() || null
            };
            const data = singleAddIp
                ? await request(root.dataset.addSingleUrl, { ...details, ipAddress: singleAddIp })
                : await request(root.dataset.addUrl, { ...details, devices: picks.map(item => ({ ipAddress: item.ipAddress, name: rows.querySelector(`[data-name-ip="${CSS.escape(item.ipAddress)}"]`)?.value.trim() || `Device-${item.ipAddress}`, deviceType: item.suggestedType })) });
            showMessage(`${data.message || 'IPAM records saved.'}${data.error ? ` ${data.error}` : ''}`, !data.success || !!data.error);
            if (data.addedIpAddresses?.length) {
                const added = new Map((data.addedRecords || []).map(item => [item.ipAddress, item.recordId]));
                discovered = discovered.map(item => added.has(item.ipAddress) ? { ...item, alreadyInInventory: true, existingIpamRecordId: added.get(item.ipAddress) } : item);
                selected.clear(); singleAddIp = null; render();
            }
        } catch (error) { showMessage(error.message, true); }
        finally { addButton.textContent = 'Add selected to IPAM'; setSelected(); }
    });
})();
