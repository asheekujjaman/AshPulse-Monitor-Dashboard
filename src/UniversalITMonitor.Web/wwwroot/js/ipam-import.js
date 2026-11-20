(() => {
    const root = document.getElementById('ipam-import');
    if (!root) return;

    const fields = [
        ['IpAddress', 'IP address', true], ['SubnetCidr', 'Subnet CIDR', false], ['VlanId', 'VLAN ID', false], ['VlanName', 'VLAN name', false],
        ['DeviceId', 'Existing device ID', false], ['DeviceName', 'Device name', false], ['DeviceType', 'Device type', false],
        ['Location', 'Location name', false], ['Building', 'Building name', false], ['Group', 'Group name', false],
        ['SwitchName', 'Switch name', false], ['SwitchIpAddress', 'Switch IP', false], ['SwitchPort', 'Switch port', false],
        ['MacAddress', 'MAC address', false], ['AssignedTo', 'Assigned to', false], ['Department', 'Department', false],
        ['Description', 'Description', false], ['AssignmentStatus', 'Assignment status', false],
        ['ReservationNote', 'Reservation note', false], ['ReservedUntilUtc', 'Reserved until (ISO 8601)', false]
    ];
    let csvText = '';
    let mappings = {};

    const fileInput = document.getElementById('ipam-csv-file');
    const mappingPanel = document.getElementById('ipam-mapping');
    const mappingFields = document.getElementById('ipam-mapping-fields');
    const message = document.getElementById('ipam-import-message');
    const previewArea = document.getElementById('ipam-preview-area');
    const commitButton = document.getElementById('ipam-commit');

    function notify(text, error = false) {
        message.textContent = text || '';
        message.classList.toggle('is-visible', Boolean(text));
        message.classList.toggle('is-error', error);
    }

    function csrfToken() {
        return document.querySelector('#ipam-antiforgery input[name="__RequestVerificationToken"]')?.value || '';
    }

    async function send(url, body) {
        const response = await fetch(url, {
            method: 'POST', credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': csrfToken() },
            body: JSON.stringify(body)
        });
        const data = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(data.error || `Request failed (${response.status}).`);
        return data;
    }

    function parseHeader(text) {
        const firstLine = text.split(/\r?\n/, 1)[0] || '';
        const headers = [];
        let field = '';
        let quoted = false;
        for (let index = 0; index < firstLine.length; index++) {
            const character = firstLine[index];
            if (quoted) {
                if (character === '"' && firstLine[index + 1] === '"') { field += '"'; index++; }
                else if (character === '"') quoted = false;
                else field += character;
            } else if (character === '"') quoted = true;
            else if (character === ',') { headers.push(field.trim()); field = ''; }
            else field += character;
        }
        headers.push(field.trim());
        if (headers.length && headers[0].charCodeAt(0) === 0xfeff) headers[0] = headers[0].slice(1);
        return headers;
    }

    function normalized(value) { return String(value || '').replace(/[^a-z0-9]/gi, '').toLowerCase(); }

    function buildMapping(headers) {
        mappingFields.replaceChildren();
        mappings = {};
        for (const [key, label, required] of fields) {
            const wrapper = document.createElement('label');
            wrapper.className = `ipam-mapping-field${required ? ' required' : ''}`;
            wrapper.append(document.createTextNode(label));
            const select = document.createElement('select');
            select.dataset.field = key;
            const skip = document.createElement('option');
            skip.value = '';
            skip.textContent = 'Skip column';
            select.append(skip);
            for (const header of headers) {
                const option = document.createElement('option');
                option.value = header;
                option.textContent = header || '(empty header)';
                select.append(option);
            }
            const aliases = key === 'IpAddress' ? ['ipaddress', 'ip'] : [normalized(key)];
            const matching = headers.find(header => aliases.includes(normalized(header)));
            if (matching) select.value = matching;
            if (required) select.required = true;
            select.addEventListener('change', collectMapping);
            wrapper.append(select);
            mappingFields.append(wrapper);
        }
        collectMapping();
    }

    function collectMapping() {
        mappings = {};
        for (const select of mappingFields.querySelectorAll('select')) mappings[select.dataset.field] = select.value || null;
        document.getElementById('ipam-preview').disabled = !mappings.IpAddress;
    }

    fileInput.addEventListener('change', async () => {
        const file = fileInput.files?.[0];
        if (!file) return;
        if (file.size > 5_000_000) { notify('CSV files must be 5 MB or smaller.', true); return; }
        csvText = (await file.text()).replace(/^\uFEFF/, '');
        const headers = parseHeader(csvText);
        if (!headers.length || headers.every(header => !header)) { notify('The CSV needs a header row.', true); return; }
        buildMapping(headers);
        mappingPanel.hidden = false;
        previewArea.hidden = true;
        notify(`${file.name} loaded. Review the column mapping before previewing.`);
    });

    document.getElementById('ipam-preview').addEventListener('click', async () => {
        collectMapping();
        try {
            const preview = await send(root.dataset.previewUrl, { csv: csvText, mapping: mappings });
            renderPreview(preview);
        } catch (error) { notify(error.message, true); }
    });

    function renderPreview(preview) {
        previewArea.hidden = false;
        const rows = document.getElementById('ipam-preview-rows');
        rows.replaceChildren();
        if (preview.error) {
            notify(preview.error, true);
            document.getElementById('ipam-preview-summary').textContent = 'Preview could not be completed.';
            commitButton.disabled = true;
            return;
        }
        notify('');
        document.getElementById('ipam-preview-summary').textContent = `${preview.validCount} ready · ${preview.invalidCount} invalid · ${preview.duplicateCount} duplicates will be skipped. Showing up to 100 rows.`;
        for (const row of (preview.rows || []).slice(0, 100)) {
            const record = row.record || {};
            const tr = document.createElement('tr');
            const result = row.duplicate ? 'Duplicate — skipped' : row.errors?.length ? row.errors.join(' ') : 'Ready to import';
            for (const value of [row.rowNumber, record.ipAddress || '—', record.deviceName || '—', record.assignmentStatus || 'Unknown']) {
                const cell = document.createElement('td'); cell.textContent = value; tr.append(cell);
            }
            const status = document.createElement('td');
            status.className = row.errors?.length || row.duplicate ? 'ipam-preview-error' : 'ipam-preview-ok';
            status.textContent = result;
            tr.append(status);
            rows.append(tr);
        }
        commitButton.disabled = preview.validCount < 1;
    }

    commitButton.addEventListener('click', async () => {
        if (!csvText || !mappings.IpAddress) return;
        commitButton.disabled = true;
        commitButton.textContent = 'Importing…';
        try {
            const report = await send(root.dataset.importUrl, { csv: csvText, mapping: mappings });
            const duplicates = report.duplicateIpAddresses?.length ? ` Duplicate addresses: ${report.duplicateIpAddresses.join(', ')}.` : '';
            const errors = report.errors?.length ? ` ${report.errors.length} row(s) need correction.` : '';
            notify(`Import finished: ${report.importedCount} added, ${report.invalidCount} invalid, ${report.duplicateCount} duplicate(s) skipped.${duplicates}${errors}`);
            commitButton.textContent = 'Import complete';
        } catch (error) {
            notify(error.message, true);
            commitButton.disabled = false;
            commitButton.textContent = 'Import valid rows';
        }
    });
})();
