(() => {
    const root = document.getElementById('device-discovery');
    if (!root) return;

    const scanForm = document.getElementById('discovery-scan-form');
    const scanButton = document.getElementById('scan-subnet-button');
    const cidrInput = document.getElementById('subnet-cidr');
    const registeredSubnet = document.getElementById('registered-subnet');
    const timeoutInput = document.getElementById('scan-timeout');
    const message = document.getElementById('discovery-message');
    const results = document.getElementById('discovery-results');
    const summary = document.getElementById('discovery-result-summary');
    const rows = document.getElementById('discovery-device-rows');
    const selectAll = document.getElementById('select-all-discovered');
    const selectionCount = document.getElementById('discovery-selection-count');
    const addSelected = document.getElementById('add-selected-devices');
    const dialog = document.getElementById('add-device-dialog');
    const addForm = document.getElementById('add-device-form');
    const dialogTitle = document.getElementById('add-dialog-title');
    const dialogIntro = document.getElementById('add-dialog-intro');
    const singleFields = document.getElementById('single-device-fields');
    const batchSummary = document.getElementById('batch-device-summary');
    const nameInput = document.getElementById('add-device-name');
    const ipSummary = document.getElementById('add-device-ip');
    const batchCount = document.getElementById('add-device-count');
    const batchDeviceList = document.getElementById('add-device-list');
    const typeInput = document.getElementById('add-device-type');
    const methodInput = document.getElementById('add-monitoring-method');
    const locationInput = document.getElementById('add-location');
    const buildingInput = document.getElementById('add-building');
    const groupInput = document.getElementById('add-group');
    const monitoringEnabledInput = document.getElementById('add-monitoring-enabled');
    const descriptionInput = document.getElementById('add-device-description');
    const snmpFields = document.getElementById('discovery-snmp-fields');
    const snmpVersion = document.getElementById('add-snmp-version');
    const snmpPort = document.getElementById('add-snmp-port');
    const snmpCommunity = document.getElementById('add-snmp-community');
    const snmpUserName = document.getElementById('add-snmp-username');
    const snmpSecurity = document.getElementById('add-snmp-security');
    const snmpAuthProtocol = document.getElementById('add-snmp-auth-protocol');
    const snmpAuthPassword = document.getElementById('add-snmp-auth-password');
    const snmpPrivacyProtocol = document.getElementById('add-snmp-privacy-protocol');
    const snmpPrivacyPassword = document.getElementById('add-snmp-privacy-password');
    const dialogError = document.getElementById('add-dialog-error');
    const confirmAdd = document.getElementById('confirm-add-device');

    const typeNames = [
        'Router', 'CoreSwitch', 'DistributionSwitch', 'AccessSwitch', 'Firewall',
        'WirelessAccessPoint', 'Server', 'Printer', 'Storage', 'Ups', 'Workstation', 'Other'
    ];
    let discoveredDevices = [];
    let selectedIps = new Set();
    let pendingDevices = [];
    let batchMode = false;

    const getCsrfToken = () => document.querySelector('#discovery-antiforgery input[name="__RequestVerificationToken"]')?.value || '';

    const requestJson = async (url, body) => {
        const response = await fetch(url, {
            method: 'POST',
            credentials: 'same-origin',
            cache: 'no-store',
            headers: {
                'Accept': 'application/json',
                'Content-Type': 'application/json',
                'RequestVerificationToken': getCsrfToken()
            },
            body: JSON.stringify(body)
        });
        const data = await response.json().catch(() => ({}));
        if (!response.ok) {
            throw new Error(data.error || data.title || `Request failed (${response.status}).`);
        }
        return data;
    };

    const showMessage = (text, kind = 'success') => {
        message.textContent = text;
        message.className = `discovery-message is-visible${kind === 'error' ? ' is-error' : kind === 'info' ? ' is-info' : ''}`;
        message.hidden = !text;
    };

    const setScanning = scanning => {
        scanButton.disabled = scanning;
        scanButton.textContent = scanning ? 'Scanning…' : 'Scan subnet';
        scanForm.setAttribute('aria-busy', scanning ? 'true' : 'false');
    };

    const typeLabel = type => {
        const labels = {
            CoreSwitch: 'Core switch',
            DistributionSwitch: 'Distribution switch',
            AccessSwitch: 'Access switch',
            WirelessAccessPoint: 'Wireless access point',
            Ups: 'UPS',
            Workstation: 'PC / workstation'
        };
        return labels[type] || type || 'Other';
    };

    const createCell = (text, className) => {
        const cell = document.createElement('td');
        if (className) cell.className = className;
        cell.textContent = text;
        return cell;
    };

    const updateSelection = () => {
        const selectable = discoveredDevices.filter(device => !device.alreadyInInventory);
        const selectedCount = selectedIps.size;
        selectionCount.textContent = `${selectedCount} selected`;
        addSelected.disabled = selectedCount === 0;
        selectAll.disabled = selectable.length === 0;
        selectAll.checked = selectable.length > 0 && selectable.every(device => selectedIps.has(device.ipAddress));
        selectAll.indeterminate = selectedCount > 0 && !selectAll.checked;
    };

    const renderResults = () => {
        rows.replaceChildren();
        for (const device of discoveredDevices) {
            const row = document.createElement('tr');
            row.dataset.ipAddress = device.ipAddress;

            const selectionCell = document.createElement('td');
            const checkbox = document.createElement('input');
            checkbox.type = 'checkbox';
            checkbox.className = 'discovery-row-select';
            checkbox.dataset.ipAddress = device.ipAddress;
            checkbox.checked = selectedIps.has(device.ipAddress);
            checkbox.disabled = device.alreadyInInventory;
            checkbox.setAttribute('aria-label', `Select ${device.ipAddress}`);
            selectionCell.append(checkbox);
            row.append(selectionCell);

            const ipCell = document.createElement('td');
            const ip = document.createElement('code');
            ip.textContent = device.ipAddress;
            ip.classList.add('copyable-ip');
            ip.dataset.copyIp = device.ipAddress;
            ipCell.append(ip);
            row.append(ipCell);

            const nameCell = document.createElement('td');
            const suggestedName = document.createElement('strong');
            suggestedName.textContent = device.suggestedName || `Device-${device.ipAddress}`;
            nameCell.append(suggestedName);
            if (device.hostname && device.hostname !== device.suggestedName) {
                const hostName = document.createElement('small');
                hostName.textContent = device.hostname;
                nameCell.append(hostName);
            }
            row.append(nameCell);
            row.append(createCell(typeLabel(device.suggestedType)));
            row.append(createCell(`${device.responseTimeMilliseconds ?? 0} ms`));

            const inventoryCell = document.createElement('td');
            const inventoryStatus = document.createElement('span');
            inventoryStatus.className = `device-status ${device.alreadyInInventory ? 'status-unknown' : 'status-up'}`;
            const dot = document.createElement('span');
            dot.className = 'status-dot';
            inventoryStatus.append(dot, document.createTextNode(device.alreadyInInventory ? 'IN INVENTORY' : 'NEW'));
            inventoryCell.append(inventoryStatus);
            row.append(inventoryCell);

            const actionCell = document.createElement('td');
            const action = document.createElement('button');
            action.type = 'button';
            action.className = device.alreadyInInventory ? 'app-secondary-button discovery-row-action is-added' : 'app-secondary-button discovery-row-action';
            action.dataset.addIp = device.ipAddress;
            action.disabled = device.alreadyInInventory;
            action.textContent = device.alreadyInInventory ? (device.existingDeviceId ? 'Already added' : 'In inventory') : 'Add';
            actionCell.append(action);
            row.append(actionCell);
            rows.append(row);
        }
        updateSelection();
    };

    const enumValue = input => Number.parseInt(input.value, 10);
    const nullableGuid = input => input.value || null;

    const readCommonFields = () => ({
        type: enumValue(typeInput),
        monitoringMethod: enumValue(methodInput),
        locationId: nullableGuid(locationInput),
        buildingId: nullableGuid(buildingInput),
        groupId: nullableGuid(groupInput),
        description: descriptionInput.value.trim() || null,
        monitoringEnabled: monitoringEnabledInput.checked,
        snmpVersion: enumValue(snmpVersion),
        snmpPort: Number.parseInt(snmpPort.value, 10) || 161,
        snmpCommunity: snmpCommunity.value || null,
        snmpUserName: snmpUserName.value.trim() || null,
        snmpSecurityLevel: enumValue(snmpSecurity),
        snmpAuthenticationProtocol: enumValue(snmpAuthProtocol),
        snmpAuthenticationPassword: snmpAuthPassword.value || null,
        snmpPrivacyProtocol: enumValue(snmpPrivacyProtocol),
        snmpPrivacyPassword: snmpPrivacyPassword.value || null
    });

    const setSuggestedType = device => {
        const index = typeNames.indexOf(device.suggestedType);
        typeInput.value = String(index >= 0 ? index : typeNames.length - 1);
    };

    const updateSnmpVisibility = () => {
        const usesSnmp = methodInput.value === '1';
        const usesV2 = snmpVersion.value === '0';
        const security = snmpSecurity.value;
        snmpFields.hidden = !usesSnmp;
        document.querySelectorAll('.discovery-snmp-v2').forEach(field => { field.hidden = !usesSnmp || !usesV2; });
        document.querySelectorAll('.discovery-snmp-v3').forEach(field => { field.hidden = !usesSnmp || usesV2; });
        document.querySelectorAll('.discovery-snmp-auth').forEach(field => { field.hidden = !usesSnmp || usesV2 || security === '0'; });
        document.querySelectorAll('.discovery-snmp-privacy').forEach(field => { field.hidden = !usesSnmp || usesV2 || security !== '2'; });
    };

    const openAddDialog = (devices, isBatch) => {
        pendingDevices = devices;
        batchMode = isBatch;
        addForm.reset();
        typeInput.value = String(typeNames.length - 1);
        dialogError.textContent = '';
        dialogError.hidden = true;
        confirmAdd.disabled = false;
        confirmAdd.textContent = isBatch ? `Add ${devices.length} devices` : 'Add to inventory';
        singleFields.hidden = isBatch;
        batchSummary.hidden = !isBatch;
        nameInput.disabled = isBatch;
        nameInput.required = !isBatch;

        if (isBatch) {
            dialogTitle.textContent = `Add ${devices.length} selected devices`;
            dialogIntro.textContent = 'These shared settings will be applied to every selected device. Device names use the discovered hostname where available.';
            batchCount.textContent = `${devices.length} device(s)`;
            batchDeviceList.replaceChildren();
            devices.slice(0, 8).forEach(device => {
                const item = document.createElement('span');
                item.textContent = `${device.suggestedName || `Device-${device.ipAddress}`} · ${device.ipAddress}`;
                batchDeviceList.append(item);
            });
            if (devices.length > 8) {
                const more = document.createElement('span');
                more.className = 'discovery-batch-more';
                more.textContent = `+${devices.length - 8} more`;
                batchDeviceList.append(more);
            }
        } else {
            batchDeviceList.replaceChildren();
            const device = devices[0];
            dialogTitle.textContent = 'Add discovered device';
            dialogIntro.textContent = 'Review the suggested name and add inventory details before saving this device.';
            nameInput.value = device.suggestedName || `Device-${device.ipAddress}`;
            ipSummary.textContent = device.ipAddress;
            setSuggestedType(device);
        }

        updateSnmpVisibility();
        dialog.showModal();
        if (!isBatch) nameInput.focus();
    };

    const markInventoryResults = result => {
        const added = new Set(result.addedIpAddresses || []);
        const skipped = new Set(result.skippedIpAddresses || []);
        for (const device of discoveredDevices) {
            if (added.has(device.ipAddress) || skipped.has(device.ipAddress)) {
                device.alreadyInInventory = true;
                selectedIps.delete(device.ipAddress);
            }
        }
        renderResults();
    };

    if (registeredSubnet) {
        registeredSubnet.addEventListener('change', () => {
            if (registeredSubnet.value) cidrInput.value = registeredSubnet.value;
        });
    }

    scanForm.addEventListener('submit', async event => {
        event.preventDefault();
        if (!cidrInput.value.trim()) {
            showMessage('Enter a subnet in CIDR notation, for example 192.168.1.0/24.', 'error');
            cidrInput.focus();
            return;
        }

        results.hidden = true;
        rows.replaceChildren();
        selectedIps.clear();
        showMessage(`Scanning ${cidrInput.value.trim()} from the application server…`, 'info');
        setScanning(true);
        try {
            const data = await requestJson(root.dataset.scanUrl, {
                cidr: cidrInput.value.trim(),
                timeoutMilliseconds: Number.parseInt(timeoutInput.value, 10) || 500
            });
            if (!data.success) {
                showMessage(data.error || 'The subnet scan could not be completed.', 'error');
                return;
            }

            discoveredDevices = Array.isArray(data.devices) ? data.devices : [];
            for (const device of discoveredDevices) {
                device.alreadyInInventory = Boolean(device.alreadyInInventory);
            }
            cidrInput.value = data.cidr || cidrInput.value;
            summary.textContent = `Scanned ${data.totalScanned} IP address(es) in ${data.cidr}. ${data.responsiveCount} host(s) responded to ICMP.`;
            renderResults();
            results.hidden = false;
            showMessage(discoveredDevices.length > 0
                ? `Found ${discoveredDevices.length} responsive device(s). Select one or more devices to add them to inventory.`
                : 'No devices responded to ICMP on this subnet.', discoveredDevices.length > 0 ? 'success' : 'info');
        } catch (error) {
            showMessage(error instanceof Error ? error.message : 'The subnet scan failed. Try again.', 'error');
        } finally {
            setScanning(false);
        }
    });

    selectAll.addEventListener('change', () => {
        if (selectAll.checked) {
            discoveredDevices.filter(device => !device.alreadyInInventory).forEach(device => selectedIps.add(device.ipAddress));
        } else {
            selectedIps.clear();
        }
        renderResults();
    });

    rows.addEventListener('change', event => {
        const checkbox = event.target.closest('.discovery-row-select');
        if (!checkbox) return;
        if (checkbox.checked) selectedIps.add(checkbox.dataset.ipAddress);
        else selectedIps.delete(checkbox.dataset.ipAddress);
        updateSelection();
    });

    rows.addEventListener('click', event => {
        const button = event.target.closest('[data-add-ip]');
        if (!button || button.disabled) return;
        const device = discoveredDevices.find(item => item.ipAddress === button.dataset.addIp);
        if (device) openAddDialog([device], false);
    });

    addSelected.addEventListener('click', () => {
        const selected = discoveredDevices.filter(device => selectedIps.has(device.ipAddress) && !device.alreadyInInventory);
        if (selected.length > 0) openAddDialog(selected, true);
    });

    document.getElementById('close-add-dialog').addEventListener('click', () => dialog.close());
    document.getElementById('cancel-add-device').addEventListener('click', () => dialog.close());
    methodInput.addEventListener('change', updateSnmpVisibility);
    snmpVersion.addEventListener('change', updateSnmpVisibility);
    snmpSecurity.addEventListener('change', updateSnmpVisibility);

    addForm.addEventListener('submit', async event => {
        event.preventDefault();
        dialogError.textContent = '';
        dialogError.hidden = true;
        confirmAdd.disabled = true;
        confirmAdd.textContent = 'Adding…';
        try {
            const common = readCommonFields();
            let data;
            if (batchMode) {
                const { type, monitoringMethod, ...sharedFields } = common;
                data = await requestJson(root.dataset.batchAddUrl, {
                    devices: pendingDevices.map(device => ({
                        name: device.suggestedName || `Device-${device.ipAddress}`,
                        ipAddress: device.ipAddress
                    })),
                    commonType: type,
                    commonMonitoringMethod: monitoringMethod,
                    ...sharedFields
                });
            } else {
                data = await requestJson(root.dataset.singleAddUrl, {
                    name: nameInput.value.trim(),
                    ipAddress: pendingDevices[0].ipAddress,
                    ...common
                });
            }

            if (!data.success) {
                dialogError.textContent = data.error || data.message || 'The device could not be added.';
                dialogError.hidden = false;
                return;
            }

            markInventoryResults(data);
            dialog.close();
            showMessage([data.message, data.error].filter(Boolean).join(' '), data.error ? 'info' : 'success');
        } catch (error) {
            dialogError.textContent = error instanceof Error ? error.message : 'The device could not be added. Try again.';
            dialogError.hidden = false;
        } finally {
            confirmAdd.disabled = false;
            confirmAdd.textContent = batchMode ? `Add ${pendingDevices.length} devices` : 'Add to inventory';
        }
    });

    dialog.addEventListener('close', () => {
        addForm.reset();
        pendingDevices = [];
        updateSnmpVisibility();
    });
})();
