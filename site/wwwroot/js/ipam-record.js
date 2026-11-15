(() => {
    const deviceSelect = document.getElementById('ipam-linked-device');
    if (!deviceSelect) return;
    const fields = {
        ip: document.querySelector('[name="Record.IpAddress"]'),
        name: document.querySelector('[name="Record.DeviceName"]'),
        type: document.querySelector('[name="Record.DeviceType"]'),
        location: document.getElementById('ipam-location'),
        building: document.getElementById('ipam-building'),
        group: document.querySelector('[name="Record.GroupId"]')
    };

    function sync() {
        const option = deviceSelect.selectedOptions[0];
        const linked = Boolean(deviceSelect.value);
        if (linked) {
            fields.ip.value = option.dataset.ip || '';
            fields.name.value = option.dataset.name || '';
            fields.type.value = option.dataset.type || '';
            fields.location.value = option.dataset.location || '';
            fields.building.value = option.dataset.building || '';
            fields.group.value = option.dataset.group || '';
        }
        for (const field of Object.values(fields)) {
            field.readOnly = linked && field.tagName === 'INPUT';
            field.disabled = linked && field.tagName === 'SELECT';
        }
    }

    deviceSelect.addEventListener('change', sync);
    sync();
})();
