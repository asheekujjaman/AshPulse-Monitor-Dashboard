(() => {
    const root = document.getElementById('network-map-editor');
    if (!root) return;

    const canvas = document.getElementById('map-canvas');
    const nodeLayer = document.getElementById('map-nodes');
    const connectionLayer = document.getElementById('map-connections');
    const annotationLayer = document.getElementById('map-annotations');
    const deviceList = document.getElementById('map-device-list');
    const entityList = document.getElementById('map-entity-list');
    const connectionList = document.getElementById('map-connection-list');
    const inspector = document.getElementById('map-inspector-content');
    const message = document.getElementById('map-message');
    const saveButton = document.getElementById('map-save');
    const nameInput = document.getElementById('map-name');
    const importDialog = document.getElementById('map-import-dialog');
    const canEdit = root.dataset.canEdit === 'true';
    const editorUrl = root.dataset.editorDataUrl;
    const svgNs = 'http://www.w3.org/2000/svg';

    const SIZES = {
        sm: { width: 100, height: 80, label: 'Small' },
        md: { width: 130, height: 100, label: 'Medium' },
        lg: { width: 170, height: 130, label: 'Big' }
    };
    let currentDefaultSize = 'md';
    const MAP_WIDTH = 1600;
    const MAP_HEIGHT = 1000;
    const ICON_GROUPS = [
        { label: 'Network', values: ['Router', 'Switch', 'CoreSwitch', 'DistributionSwitch', 'AccessSwitch', 'Firewall', 'AccessPoint', 'WirelessAccessPoint', 'Modem', 'Gateway', 'LoadBalancer', 'Vpn', 'Internet', 'Cloud', 'Fiber', 'Subnet'] },
        { label: 'Compute and storage', values: ['Server', 'VirtualMachine', 'Hypervisor', 'Storage', 'Database', 'Backup', 'Rack'] },
        { label: 'Endpoints and security', values: ['Device', 'Pc', 'Laptop', 'MobilePhone', 'Tablet', 'VoipPhone', 'Printer', 'Monitor', 'Camera', 'Nvr', 'SecuritySensor'] },
        { label: 'Power', values: ['Ups', 'PowerDistributionUnit'] },
        { label: 'Sites and groups', values: ['Site', 'Building', 'Group'] }
    ];
    const ICON_LABELS = {
        Pc: 'PC / Workstation', Ups: 'UPS', VoipPhone: 'VoIP Phone', Nvr: 'NVR', Vpn: 'VPN',
        PowerDistributionUnit: 'Power Distribution Unit', CoreSwitch: 'Core Switch',
        DistributionSwitch: 'Distribution Switch', AccessSwitch: 'Access Switch',
        WirelessAccessPoint: 'Wireless Access Point', VirtualMachine: 'Virtual Machine',
        LoadBalancer: 'Load Balancer', SecuritySensor: 'Security Sensor'
    };

    const state = {
        map: null,
        devices: [],
        deviceById: new Map(),
        groups: [],
        groupById: new Map(),
        mapLocations: [],
        mapLocationById: new Map(),
        nodeById: new Map(),
        deviceNodeById: new Map(),
        locations: [],
        buildings: [],
        selectedNodeId: null,
        selectedDeviceId: null,
        selectedConnectionId: null,
        selectedAnnotationId: null,
        connectStartId: null,
        toolMode: 'select',
        locked: false,
        zoom: 1,
        panX: 0,
        panY: 0,
        pointer: null,
        dirty: false,
        relatedDeviceCache: new Map(),
        aggregateStatusCache: new Map(),
        nodeElements: new Map(),
        connectionsByNodeId: new Map(),
        viewMode: false
    };

    // Modern High-Fidelity Network & Infrastructure Icons (48x48 viewport)
    const icons = {
        Router: [
            'M24 6a18 18 0 1 0 0 36 18 18 0 0 0 0-36z',
            'M24 14v20m-10-10h20',
            'm28 17 4-3-4-3m-8 6-4-3 4-3m6 14 4 3-4 3m-8-6-4 3 4 3'
        ],
        Switch: [
            'M6 13h36v22H6z',
            'M13 21h22m-18 6h18',
            'm16 18-3 3 3 3m16 3 3 3-3 3'
        ],
        Firewall: [
            'M8 8h32v32H8z',
            'M8 18h32M8 29h32',
            'M18 8v10M30 8v10M12 18v11M24 18v11M36 18v11M18 29v11M30 29v11'
        ],
        Server: [
            'M8 8h32v13H8z',
            'M8 27h32v13H8z',
            'M14 14.5h.01M14 33.5h.01M20 14.5h16M20 33.5h16'
        ],
        AccessPoint: [
            'M24 38a3 3 0 1 0 0-6 3 3 0 0 0 0 6z',
            'M16 28a11.3 11.3 0 0 1 16 0',
            'M11 22a20 20 0 0 1 26 0',
            'M6 16a28 28 0 0 1 36 0'
        ],
        Cloud: [
            'M14 35h20a9 9 0 0 0 1.5-17.8A12 12 0 0 0 13 19a8.5 8.5 0 0 0 1 16z'
        ],
        Internet: [
            'M24 6a18 18 0 1 0 0 36 18 18 0 0 0 0-36z',
            'M6 24h36M24 6c7 7 7 29 0 36M24 6c-7 7-7 29 0 36'
        ],
        Pc: [
            'M8 8h32v22H8z',
            'M24 30v7m-8 0h16',
            'M14 14h20v10H14z'
        ],
        Printer: [
            'M14 14V7h20v7',
            'M10 14h28a3 3 0 0 1 3 3v12H35v6H13v-6H7V17a3 3 0 0 1 3-3z',
            'M15 26h18v9H15z',
            'M34 20h.01'
        ],
        Storage: [
            'M8 8h32v9H8z',
            'M8 20h32v9H8z',
            'M8 32h32v9H8z',
            'M14 12.5h.01M14 24.5h.01M14 36.5h.01M20 12.5h16M20 24.5h16M20 36.5h16'
        ],
        Ups: [
            'M13 10h22v28H13z',
            'M19 7h10v3H19z',
            'm25 18-5 7h5l-2 7 6-8h-5z'
        ],
        Group: [
            'M10 7h28v34H10z',
            'M10 17h28M10 27h28M10 37h28',
            'M15 12h.01M15 22h.01M15 32h.01M20 12h14M20 22h14M20 32h14'
        ],
        Device: ['M24 5 42 15v18L24 43 6 33V15z', 'M24 5v19m18-9L24 24 6 15m18 9v19'],
        CoreSwitch: ['M6 13h36v22H6z', 'M11 19h7v5h-7zm10 0h7v5h-7zm10 0h7v5h-7z', 'M11 28h26m-22 0v7m18-7v7'],
        DistributionSwitch: ['M6 14h36v20H6z', 'M12 20h6v5h-6zm12 0h6v5h-6zm12 0h1v5h-1z', 'M24 34v8m-10 0h20'],
        AccessSwitch: ['M7 15h34v20H7z', 'M12 21h5v5h-5zm10 0h5v5h-5zm10 0h5v5h-5z', 'M13 30h22'],
        WirelessAccessPoint: ['M24 37a3 3 0 1 0 0-6 3 3 0 0 0 0 6z', 'M15 27a13 13 0 0 1 18 0', 'M9 21a21 21 0 0 1 30 0', 'M4 15a29 29 0 0 1 40 0'],
        Site: ['M7 40V19l17-12 17 12v21H7z', 'M17 40V27h14v13', 'M13 20h.01M24 17h.01M35 20h.01'],
        Building: ['M10 40V12l16-6v34', 'M26 18l12 4v18', 'M16 18h4v4h-4zm0 8h4v4h-4zm0 8h4v4h-4zm15-10h3v4h-3zm0 8h3v4h-3z'],
        Laptop: ['M10 9h28v22H10z', 'M6 36h36l-3 4H9z', 'M17 14h14v12H17z'],
        MobilePhone: ['M15 5h18v38H15z', 'M20 10h8m-7 27h6'],
        Tablet: ['M10 5h28v38H10z', 'M21 10h6m-5 28h4'],
        VoipPhone: ['M9 14h30v23H9z', 'M14 14V9h20v5', 'M15 21h.01M21 21h.01M27 21h.01M33 21h.01', 'M17 30h14'],
        Camera: ['M7 16h34v23H7z', 'M13 16l4-7h14l4 7', 'M24 22a6 6 0 1 0 0 12 6 6 0 0 0 0-12z'],
        Nvr: ['M8 8h32v11H8z', 'M8 23h32v11H8z', 'M13 13h.01M13 28h.01M20 13h14M20 28h14', 'M24 34v7'],
        Database: ['M24 6c10 0 17 3 17 7s-7 7-17 7S7 17 7 13 14 6 24 6z', 'M7 13v22c0 4 7 7 17 7s17-3 17-7V13', 'M7 24c0 4 7 7 17 7s17-3 17-7'],
        Backup: ['M9 11h30v28H9z', 'M15 18h18M15 25h18M15 32h10', 'M24 5v6m-5-4 5-5 5 5'],
        LoadBalancer: ['M24 7v10m-14 0h28v10H10z', 'M10 32h10v9H10z', 'M28 32h10v9H28z', 'M15 27v5m18-5v5'],
        Modem: ['M7 14h34v22H7z', 'M13 29h.01M19 29h.01M25 29h.01M31 29h.01', 'M17 21a10 10 0 0 1 14 0', 'M21 24a4 4 0 0 1 6 0'],
        Gateway: ['M6 16h36v23H6z', 'M15 27h18', 'm25 20 8 7-8 7', 'M24 5v11'],
        Vpn: ['M24 5 39 11v11c0 10-6 17-15 21C15 39 9 32 9 22V11z', 'M17 24h14m-7-7v14'],
        VirtualMachine: ['M8 8h32v12H8z', 'M8 25h32v12H8z', 'M13 14h.01M13 31h.01M19 14h15M19 31h15'],
        Hypervisor: ['M8 9h32v11H8z', 'M8 28h32v11H8z', 'M24 20v8m-5-4h10', 'M13 14h.01M13 33h.01'],
        Rack: ['M12 5h24v38H12z', 'M16 10h16v8H16z', 'M16 21h16v8H16z', 'M16 32h16v7H16z', 'M19 14h.01M19 25h.01M19 35h.01'],
        Monitor: ['M7 8h34v25H7z', 'M24 33v7m-9 0h18', 'M13 14h22v13H13z'],
        PowerDistributionUnit: ['M15 6h18v36H15z', 'M20 12h8v5h-8zm0 10h8v5h-8zm0 10h8v5h-8z', 'M11 11v26m26-26v26'],
        Fiber: ['M6 24h11m14 0h11', 'M16 15l9 9-9 9m16-18-9 9 9 9', 'M20 18h8v12h-8z'],
        Subnet: ['M24 7v9m-14 8h28M10 24v15m28-15v15', 'M19 6h10v10H19z', 'M5 34h10v9H5z', 'M33 34h10v9H33z'],
        SecuritySensor: ['M24 6 42 39H6z', 'M24 17v10m0 6h.01']
    };

    function iconLabel(icon) {
        return ICON_LABELS[icon] || String(icon || 'Device').replace(/([a-z0-9])([A-Z])/g, '$1 $2');
    }

    function getDefaultIconForDeviceType(type) {
        const norm = String(type || '').toLowerCase().replace(/[^a-z0-9]/g, '');
        if (norm.includes('core')) return 'CoreSwitch';
        if (norm.includes('distribution')) return 'DistributionSwitch';
        if (norm.includes('accessswitch')) return 'AccessSwitch';
        if (norm.includes('router')) return 'Router';
        if (norm.includes('switch')) return 'Switch';
        if (norm.includes('firewall')) return 'Firewall';
        if (norm.includes('point') || norm.includes('wireless') || norm.includes('wifi')) return 'WirelessAccessPoint';
        if (norm.includes('server')) return 'Server';
        if (norm.includes('virtualmachine') || norm.includes('vm')) return 'VirtualMachine';
        if (norm.includes('hypervisor')) return 'Hypervisor';
        if (norm.includes('workstation') || norm.includes('desktop') || norm.includes('pc')) return 'Pc';
        if (norm.includes('laptop') || norm.includes('notebook')) return 'Laptop';
        if (norm.includes('ups')) return 'Ups';
        if (norm.includes('pdu') || norm.includes('powerdistribution')) return 'PowerDistributionUnit';
        if (norm.includes('print')) return 'Printer';
        if (norm.includes('database')) return 'Database';
        if (norm.includes('storage') || norm.includes('nas') || norm.includes('san')) return 'Storage';
        if (norm.includes('camera') || norm.includes('cctv')) return 'Camera';
        if (norm.includes('nvr')) return 'Nvr';
        if (norm.includes('phone') || norm.includes('voip')) return 'VoipPhone';
        if (norm.includes('tablet')) return 'Tablet';
        if (norm.includes('mobile')) return 'MobilePhone';
        if (norm.includes('internet')) return 'Internet';
        if (norm.includes('cloud')) return 'Cloud';
        return 'Device';
    }

    function el(name, attrs = {}, text = null) {
        const element = document.createElement(name);
        for (const [key, value] of Object.entries(attrs)) {
            if (value !== undefined && value !== null) element.setAttribute(key, String(value));
        }
        if (text !== null) element.textContent = text;
        return element;
    }

    function svg(name, attrs = {}) {
        const element = document.createElementNS(svgNs, name);
        for (const [key, value] of Object.entries(attrs)) {
            if (value !== undefined && value !== null) element.setAttribute(key, String(value));
        }
        return element;
    }

    function notify(text, error = false) {
        message.textContent = text || '';
        message.classList.toggle('is-error', error);
        message.classList.toggle('is-visible', Boolean(text));
    }

    function csrfToken() {
        return document.querySelector('#map-antiforgery input[name="__RequestVerificationToken"]')?.value || '';
    }

    async function requestJson(url, options = {}) {
        const response = await fetch(url, {
            credentials: 'same-origin',
            ...options,
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': csrfToken(),
                ...(options.headers || {})
            }
        });
        const data = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(data.error || `Request failed (${response.status}).`);
        return data;
    }

    async function updateLiveStatus() {
        if (!state.map || document.hidden) return;
        try {
            const response = await fetch(root.dataset.statusUrl, { credentials: 'same-origin', cache: 'no-store' });
            if (!response.ok) return;
            const current = await response.json();
            const byId = new Map(current.map(device => [device.id.toLowerCase(), device]));
            let visualsChanged = false;
            let selectedDetailsChanged = false;
            const selectedNode = findNode(state.selectedNodeId);
            const selectedDeviceId = selectedNode?.kind === 'Device' ? String(selectedNode.deviceId).toLowerCase() : null;
            const selectedRelatedDeviceIds = new Set(selectedNode ? relatedDevices(selectedNode).map(device => device.id.toLowerCase()) : []);
            for (const device of state.devices) {
                const status = byId.get(device.id.toLowerCase());
                if (!status) continue;
                if (device.status !== status.status) {
                    visualsChanged = true;
                    if (selectedRelatedDeviceIds.has(device.id.toLowerCase())) selectedDetailsChanged = true;
                }
                if (selectedDeviceId === device.id.toLowerCase() && (device.status !== status.status ||
                    device.responseTimeMilliseconds !== status.responseTimeMilliseconds ||
                    device.lastCheckUtc !== status.lastCheckUtc ||
                    device.currentStateSinceUtc !== status.currentStateSinceUtc ||
                    device.monitoringEnabled !== status.monitoringEnabled)) {
                    selectedDetailsChanged = true;
                }
                device.status = status.status;
                device.responseTimeMilliseconds = status.responseTimeMilliseconds;
                device.lastCheckUtc = status.lastCheckUtc;
                device.currentStateSinceUtc = status.currentStateSinceUtc;
                device.monitoringEnabled = status.monitoringEnabled;
            }
            if (visualsChanged) {
                refreshAggregateStatusCache();
                refreshStatusVisuals();
            }
            if (state.selectedNodeId && selectedDetailsChanged) showNodeDetails(state.selectedNodeId);
        } catch { /* Suppress jitter */ }
    }

    function statusClass(status) {
        return `status-${String(status || 'Unknown').toLowerCase()}`;
    }

    function applyStatusClass(element, status) {
        if (!element) return;
        element.classList.remove('status-up', 'status-down', 'status-warning', 'status-unknown');
        element.classList.add(statusClass(status));
    }

    function refreshStatusVisuals() {
        for (const node of state.map.nodes) {
            const id = String(node.id || node.deviceId).toLowerCase();
            const group = state.nodeElements.get(id);
            if (!group) continue;
            const status = nodeStatus(node);
            group.setAttribute('aria-label', `${nodeName(node)}, ${status}`);
            applyStatusClass(group.querySelector('.map-node-badge'), status);
            const typeLabel = group.querySelector('.map-node-type');
            if (typeLabel) {
                typeLabel.textContent = `${iconLabel(node.icon || (node.kind === 'Group' ? 'Group' : node.kind === 'Location' ? 'Site' : 'Device')).toUpperCase()} · ${status.toUpperCase()}`;
                applyStatusClass(typeLabel, status);
            }
        }

        for (const card of deviceList.querySelectorAll('[data-device-id]')) {
            const device = state.deviceById.get(card.dataset.deviceId.toLowerCase());
            applyStatusClass(card.querySelector('.map-status-mark'), device?.status);
        }
        for (const card of entityList?.querySelectorAll('[data-entity-kind][data-entity-id]') || []) {
            const item = card.dataset.entityKind === 'Group'
                ? { kind: 'Group', groupId: card.dataset.entityId }
                : { kind: 'Location', locationId: card.dataset.entityId };
            applyStatusClass(card.querySelector('.map-status-mark'), nodeStatus(item));
        }
    }

    function relatedDevices(node) {
        if (node.kind === 'Device') {
            const device = state.deviceById.get(String(node.deviceId || '').toLowerCase());
            return device ? [device] : [];
        }
        const entityId = String(node.kind === 'Group' ? node.groupId : node.locationId || node.id || '').toLowerCase();
        return state.relatedDeviceCache.get(`${node.kind}:${entityId}`) || [];
    }

    function relatedDeviceKey(node) {
        const entityId = String(node.kind === 'Group' ? node.groupId : node.locationId || node.id || '').toLowerCase();
        return `${node.kind}:${entityId}`;
    }

    function refreshAggregateStatusCache() {
        const statusCache = state.aggregateStatusCache;
        statusCache.clear();
        for (const [key, devices] of state.relatedDeviceCache) {
            if (!devices.length) statusCache.set(key, 'Unknown');
            else if (devices.some(device => device.status === 'Down')) statusCache.set(key, 'Down');
            else if (devices.every(device => device.status === 'Up')) statusCache.set(key, 'Up');
            else if (devices.some(device => device.status === 'Warning')) statusCache.set(key, 'Warning');
            else statusCache.set(key, 'Unknown');
        }
    }

    function buildRelatedDeviceCache() {
        const cache = state.relatedDeviceCache;
        cache.clear();
        const groupsById = new Map(state.groups.map(group => [group.id.toLowerCase(), group]));
        const locationsById = new Map(state.mapLocations.map(location => [location.id.toLowerCase(), location]));
        const add = (kind, id, device) => {
            if (!id) return;
            const key = `${kind}:${id.toLowerCase()}`;
            if (!cache.has(key)) cache.set(key, []);
            cache.get(key).push(device);
        };

        for (const device of state.devices) {
            const visitedGroups = new Set();
            let groupId = String(device.groupId || '').toLowerCase();
            while (groupId && !visitedGroups.has(groupId)) {
                visitedGroups.add(groupId);
                add('Group', groupId, device);
                groupId = String(groupsById.get(groupId)?.parentGroupId || '').toLowerCase();
            }

            const visitedLocations = new Set();
            const pendingLocations = [device.locationId, device.buildingId].filter(Boolean).map(id => String(id).toLowerCase());
            while (pendingLocations.length) {
                const locationId = pendingLocations.pop();
                if (!locationId || visitedLocations.has(locationId)) continue;
                visitedLocations.add(locationId);
                add('Location', locationId, device);
                const parentId = String(locationsById.get(locationId)?.parentLocationId || '').toLowerCase();
                if (parentId) pendingLocations.push(parentId);
            }
        }
    }

    function nodeStatus(node) {
        if (node.kind !== 'Device') return state.aggregateStatusCache.get(relatedDeviceKey(node)) || 'Unknown';
        const devices = relatedDevices(node);
        if (!devices.length) return 'Unknown';
        if (devices.some(device => device.status === 'Down')) return 'Down';
        if (devices.every(device => device.status === 'Up')) return 'Up';
        if (devices.some(device => device.status === 'Warning')) return 'Warning';
        return 'Unknown';
    }

    function nodeName(node) {
        if (node.kind === 'Device') return state.deviceById.get(String(node.deviceId || '').toLowerCase())?.name || 'Missing device';
        if (node.kind === 'Group') return state.groupById.get(String(node.groupId || '').toLowerCase())?.name || 'Missing group';
        return state.mapLocationById.get(String(node.locationId || '').toLowerCase())?.name || 'Missing site';
    }

    function nodeSubtitle(node) {
        if (node.kind === 'Device') return state.deviceById.get(String(node.deviceId || '').toLowerCase())?.ipAddress || '';
        return `${relatedDevices(node).length} devices`;
    }

    function findNode(id) {
        return state.nodeById.get(String(id || '').toLowerCase());
    }

    function connectionNode(connection, from) {
        const nodeId = from ? connection.fromNodeId : connection.toNodeId;
        const deviceId = from ? connection.fromDeviceId : connection.toDeviceId;
        return nodeId ? findNode(nodeId) : state.deviceNodeById.get(String(deviceId || '').toLowerCase());
    }

    function rebuildNodeIndex() {
        state.nodeById.clear();
        state.deviceNodeById.clear();
        for (const node of state.map.nodes) {
            const id = String(node.id || node.deviceId).toLowerCase();
            state.nodeById.set(id, node);
            if (node.deviceId) state.nodeById.set(String(node.deviceId).toLowerCase(), node);
            if (node.kind === 'Device' && node.deviceId) state.deviceNodeById.set(String(node.deviceId).toLowerCase(), node);
        }
    }

    function nodeGeometry(node) {
        const width = Number(node.width) || 130;
        const height = Number(node.height) || 100;
        const isSmall = width <= 110 || height <= 85;
        const isBig = width >= 165;
        const badgeRadius = isSmall ? 18 : isBig ? 28 : 22;
        const centerX = width / 2;
        const badgeY = badgeRadius + 4;
        const nameY = badgeY + badgeRadius + (isSmall ? 13 : 15);
        const subtitleY = nameY + (isSmall ? 11 : 14);
        const typeY = subtitleY + (isSmall ? 10 : 13);
        return {
            width, height, centerX, badgeY, badgeRadius,
            iconScale: badgeRadius / 24,
            nameY,
            subtitleY,
            typeY,
            nameFontSize: isSmall ? 9.5 : isBig ? 13 : 11,
            ipFontSize: isSmall ? 8 : isBig ? 10.5 : 9,
            typeFontSize: isSmall ? 7 : isBig ? 9 : 8
        };
    }

    function formatDate(value) {
        if (!value) return '—';
        const date = new Date(value);
        return Number.isNaN(date.getTime()) ? '—' : date.toLocaleString();
    }

    function durationSince(value) {
        if (!value) return 'Not checked';
        const seconds = Math.max(0, Math.floor((Date.now() - new Date(value).getTime()) / 1000));
        const days = Math.floor(seconds / 86400);
        const hours = Math.floor((seconds % 86400) / 3600);
        const minutes = Math.floor((seconds % 3600) / 60);
        return `${days ? `${days}d ` : ''}${hours}h ${minutes}m`;
    }

    function markDirty() {
        state.dirty = true;
        saveButton.disabled = !canEdit;
        saveButton.textContent = 'Save changes';
    }

    function hasSavedMap() {
        return Boolean(state.map?.id && state.map.id !== '00000000-0000-0000-0000-000000000000');
    }

    function appendOption(select, value, label) {
        const option = document.createElement('option');
        option.value = value || '';
        option.textContent = label;
        select.append(option);
    }

    function initializeHierarchy() {
        for (const location of state.locations) appendOption(document.getElementById('map-location'), location.id, location.name);
        for (const building of state.buildings) appendOption(document.getElementById('map-building'), building.id, building.name);
        document.getElementById('map-location').value = state.map.locationId || '';
        document.getElementById('map-building').value = state.map.buildingId || '';
    }

    function initialize() {
        const mapId = root.dataset.mapId;
        const url = `${editorUrl}${mapId ? `?id=${encodeURIComponent(mapId)}` : ''}`;
        fetch(url, { credentials: 'same-origin' })
            .then(response => response.ok ? response.json() : Promise.reject(new Error('Unable to load map data.')))
            .then(data => {
                state.map = data.map;
                state.map.nodes ||= [];
                state.map.connections ||= [];
                state.map.annotations ||= [];
                for (const node of state.map.nodes) {
                    node.id ||= node.deviceId;
                    node.kind ||= 'Device';
                    if (!node.width) node.width = 160;
                    if (!node.height) node.height = 128;
                }
                state.devices = data.devices || [];
                state.deviceById = new Map(state.devices.map(device => [device.id.toLowerCase(), device]));
                state.groups = data.groups || [];
                state.groupById = new Map(state.groups.map(group => [group.id.toLowerCase(), group]));
                state.mapLocations = data.mapLocations || [];
                state.mapLocationById = new Map(state.mapLocations.map(location => [location.id.toLowerCase(), location]));
                state.locations = data.locations || [];
                state.buildings = data.buildings || [];
                buildRelatedDeviceCache();
                refreshAggregateStatusCache();
                nameInput.value = state.map.name || '';
                document.getElementById('map-title').textContent = hasSavedMap() ? (state.map.name || 'Network map') : 'New network map';
                initializeHierarchy();
                document.getElementById('map-export').disabled = !hasSavedMap();
                fitMap();
                render();
            })
            .catch(error => notify(error.message, true));
    }

    function render() {
        if (!state.map) return;
        rebuildNodeIndex();
        applyViewBox();
        renderDevices();
        renderEntities();
        renderAnnotations();
        renderConnections();
        renderNodes();
        renderConnectionList();
        if (state.selectedNodeId) showNodeDetails(state.selectedNodeId);
        else if (state.selectedConnectionId) showConnectionDetails(state.selectedConnectionId);
        else if (state.selectedAnnotationId) showAnnotationDetails(state.selectedAnnotationId);
        else showEmptyInspector();
        document.getElementById('map-device-count').textContent = `${state.devices.length}`;
    }

    function renderDevices() {
        const query = document.getElementById('map-device-search').value.trim().toLowerCase();
        const placed = new Set(state.map.nodes.filter(node => node.kind === 'Device').map(node => node.deviceId.toLowerCase()));
        deviceList.replaceChildren();
        const found = state.devices.filter(device => !placed.has(device.id.toLowerCase()) &&
            `${device.name} ${device.ipAddress} ${device.deviceType}`.toLowerCase().includes(query));
        for (const device of found) {
            const card = document.createElement('div');
            card.className = 'map-device-card';
            card.draggable = canEdit && !state.locked;
            card.dataset.deviceId = device.id;
            card.addEventListener('dragstart', event => event.dataTransfer.setData('text/device-id', device.id));
            const identity = document.createElement('div');
            identity.className = 'map-device-card-identity';
            identity.append(el('span', { class: `map-status-mark ${statusClass(device.status)}` }), el('span', {}, device.name), el('small', { class: 'copyable-ip', 'data-copy-ip': device.ipAddress }, `${device.ipAddress} · ${device.deviceType}`));
            card.append(identity);
            if (canEdit && !state.locked) {
                const add = el('button', { type: 'button', class: 'map-add-device', 'aria-label': `Add ${device.name}` }, 'Add');
                add.addEventListener('click', () => addDeviceToMap(device.id, 80 + (state.map.nodes.length % 5) * 230, 70 + Math.floor(state.map.nodes.length / 5) * 165));
                card.append(add);
            }
            deviceList.append(card);
        }
        if (!found.length) deviceList.append(el('p', { class: 'map-list-empty' }, query ? 'No matching available devices.' : 'All inventory devices are on this map.'));
    }

    function renderEntities() {
        if (!entityList) return;
        const query = document.getElementById('map-entity-search').value.trim().toLowerCase();
        const entries = [
            ...state.groups.map(item => ({ id: item.id, name: item.name, kind: 'Group', groupId: item.id })),
            ...state.mapLocations.map(item => ({ id: item.id, name: item.name, kind: 'Location', locationId: item.id, locationKind: item.kind }))
        ];
        const placed = new Set(state.map.nodes.filter(node => node.kind !== 'Device')
            .map(node => `${node.kind}:${String(node.groupId || node.locationId).toLowerCase()}`));
        const available = entries.filter(item => !placed.has(`${item.kind}:${item.id.toLowerCase()}`) &&
            `${item.name} ${item.kind} ${item.locationKind || ''}`.toLowerCase().includes(query));
        entityList.replaceChildren();
        for (const item of available) {
            const card = document.createElement('div');
            card.className = 'map-device-card map-entity-card';
            card.draggable = canEdit && !state.locked;
            card.dataset.entityKind = item.kind;
            card.dataset.entityId = item.id;
            card.addEventListener('dragstart', event => {
                event.dataTransfer.setData('text/entity-json', JSON.stringify(item));
            });
            const identity = document.createElement('div');
            identity.className = 'map-device-card-identity';
            const status = nodeStatus(item);
            identity.append(el('span', { class: `map-status-mark ${statusClass(status)}` }), el('span', {}, item.name), el('small', {}, `${item.kind === 'Group' ? 'Group' : item.locationKind} · ${relatedDevices(item).length} devices`));
            card.append(identity);
            if (canEdit && !state.locked) {
                const add = el('button', { type: 'button', class: 'map-add-device', 'aria-label': `Add ${item.name}` }, 'Add');
                add.addEventListener('click', () => addEntityToMap(item, 100 + (state.map.nodes.length % 4) * 320, 100 + Math.floor(state.map.nodes.length / 4) * 190));
                card.append(add);
            }
            entityList.append(card);
        }
        if (!available.length) entityList.append(el('p', { class: 'map-list-empty' }, query ? 'No matching groups or sites.' : 'All groups and sites are on this map.'));
    }

    function renderAnnotations() {
        if (!annotationLayer) return;
        annotationLayer.replaceChildren();
        for (const annotation of state.map.annotations || []) {
            const selected = state.selectedAnnotationId?.toLowerCase() === annotation.id.toLowerCase();
            const fill = annotation.fillColor || 'none';
            let shape;
            if (annotation.kind === 'Line') {
                shape = svg('line', { x1: annotation.x, y1: annotation.y, x2: annotation.x + annotation.width, y2: annotation.y + annotation.height });
            } else if (annotation.kind === 'Ellipse') {
                shape = svg('ellipse', { cx: annotation.x + annotation.width / 2, cy: annotation.y + annotation.height / 2, rx: Math.abs(annotation.width / 2), ry: Math.abs(annotation.height / 2) });
            } else if (annotation.kind === 'Text') {
                shape = svg('text', { x: annotation.x, y: annotation.y + 20, class: 'map-annotation-text' });
                shape.textContent = annotation.text || '';
            } else {
                shape = svg('rect', { x: annotation.x, y: annotation.y, width: annotation.width, height: annotation.height, rx: 4 });
            }
            shape.setAttribute('class', `map-annotation map-annotation-${String(annotation.kind).toLowerCase()} ${selected ? 'selected' : ''}`);
            shape.setAttribute('stroke', annotation.kind === 'Text' ? 'none' : annotation.strokeColor || '#64748b');
            shape.setAttribute('stroke-width', annotation.kind === 'Text' ? '0' : String(annotation.strokeWidth || 2));
            shape.setAttribute('fill', annotation.kind === 'Text' ? annotation.strokeColor || '#64748b' : fill);
            shape.setAttribute('data-annotation-id', annotation.id);
            shape.setAttribute('tabindex', '0');
            shape.setAttribute('role', 'button');
            shape.addEventListener('pointerdown', event => beginAnnotationPointer(event, annotation));
            shape.addEventListener('click', event => { event.stopPropagation(); selectAnnotation(annotation.id); });
            annotationLayer.append(shape);
        }
    }

    function renderNodes() {
        nodeLayer.replaceChildren();
        state.nodeElements.clear();
        for (const node of state.map.nodes) {
            const id = node.id || node.deviceId;
            const title = nodeName(node);
            const status = nodeStatus(node);
            const selected = String(state.selectedNodeId || '').toLowerCase() === String(id).toLowerCase();
            const connectStart = String(state.connectStartId || '').toLowerCase() === String(id).toLowerCase();
            const { width, height, centerX, badgeY, badgeRadius, iconScale, nameY, subtitleY, typeY, nameFontSize, ipFontSize, typeFontSize } = nodeGeometry(node);
            
            const group = svg('g', {
                class: `map-node ${selected ? 'selected' : ''} ${connectStart ? 'connect-start' : ''} ${state.locked ? 'is-locked' : ''}`,
                transform: `translate(${node.x} ${node.y})`,
                'data-node-id': id,
                tabindex: '0',
                role: 'button',
                'aria-label': `${title}, ${status}`
            });

            // Clean circular icon badge with glowing status drop-shadow
            const badge = svg('circle', {
                class: `map-node-badge ${statusClass(status)}`,
                cx: centerX,
                cy: badgeY,
                r: badgeRadius
            });
            group.append(badge);

            if (selected || connectStart) {
                group.append(svg('circle', { class: 'map-node-focus', cx: centerX, cy: badgeY, r: badgeRadius + 6 }));
            }

            const iconGroup = svg('g', {
                class: 'map-device-icon',
                transform: `translate(${centerX - 24 * iconScale} ${badgeY - 24 * iconScale}) scale(${iconScale})`
            });

            const defaultIcon = node.kind === 'Location' ? 'Site' : node.kind === 'Group' ? 'Group' : 'Device';
            const iconKey = icons[node.icon] ? node.icon : defaultIcon;

            const paths = icons[iconKey] || icons.Router;
            for (const pathData of paths) {
                iconGroup.append(svg('path', { d: pathData }));
            }
            group.append(iconGroup);

            const name = title.length > 25 ? `${title.slice(0, 24)}…` : title;
            const nameEl = svg('text', { class: 'map-node-name', x: centerX, y: nameY, 'text-anchor': 'middle', style: `font-size: ${nameFontSize}px;` }, name);
            const subEl = svg('text', { class: 'map-node-ip', x: centerX, y: subtitleY, 'text-anchor': 'middle', style: `font-size: ${ipFontSize}px;` }, nodeSubtitle(node));
            const typeText = `${iconLabel(iconKey).toUpperCase()} · ${status.toUpperCase()}`;
            const typeEl = svg('text', { class: `map-node-type ${statusClass(status)}`, x: centerX, y: typeY, 'text-anchor': 'middle', style: `font-size: ${typeFontSize}px;` }, typeText);

            group.append(nameEl, subEl, typeEl);

            group.addEventListener('pointerdown', event => beginNodePointer(event, node, group));
            group.addEventListener('click', event => {
                event.stopPropagation();
                if (state.toolMode === 'select' || state.toolMode === 'connect') selectNode(id);
            });
            group.addEventListener('keydown', event => {
                if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); selectNode(id); }
            });

            nodeLayer.append(group);
            state.nodeElements.set(String(id).toLowerCase(), group);
        }
    }

    function renderConnections() {
        connectionLayer.replaceChildren();
        state.connectionsByNodeId.clear();
        for (const connection of state.map.connections) {
            const from = connectionNode(connection, true);
            const to = connectionNode(connection, false);
            if (!from || !to) continue;
            const fromGeometry = nodeGeometry(from);
            const toGeometry = nodeGeometry(to);
            const x1 = from.x + fromGeometry.centerX;
            const y1 = from.y + fromGeometry.badgeY;
            const x2 = to.x + toGeometry.centerX;
            const y2 = to.y + toGeometry.badgeY;
            const selected = state.selectedConnectionId?.toLowerCase() === connection.id.toLowerCase();
            const group = svg('g', { 'data-connection-id': connection.id });
            const line = svg('line', {
                x1, y1, x2, y2,
                class: `map-connection map-connection-${String(connection.kind || 'ethernet').toLowerCase()} ${selected ? 'selected' : ''}`,
                'stroke-width': String(connection.strokeWidth || 2.5),
                'data-from-id': from.id || from.deviceId,
                'data-to-id': to.id || to.deviceId
            });
            const fromId = String(from.id || from.deviceId).toLowerCase();
            const toId = String(to.id || to.deviceId).toLowerCase();
            if (!state.connectionsByNodeId.has(fromId)) state.connectionsByNodeId.set(fromId, []);
            if (!state.connectionsByNodeId.has(toId)) state.connectionsByNodeId.set(toId, []);
            state.connectionsByNodeId.get(fromId).push(line);
            state.connectionsByNodeId.get(toId).push(line);
            line.addEventListener('click', event => { event.stopPropagation(); selectConnection(connection.id); });
            group.append(line);

            if (connection.label) {
                const label = svg('text', {
                    x: (x1 + x2) / 2,
                    y: (y1 + y2) / 2 - 8,
                    class: 'map-connection-label'
                }, connection.label);
                label.addEventListener('click', event => { event.stopPropagation(); selectConnection(connection.id); });
                group.append(label);
            }
            connectionLayer.append(group);
        }
    }

    function updateNodeGeometryElement(node, group) {
        if (!group) return;
        const geometry = nodeGeometry(node);
        group.setAttribute('transform', `translate(${node.x} ${node.y})`);
        const badge = group.querySelector('.map-node-badge');
        badge?.setAttribute('cx', geometry.centerX);
        badge?.setAttribute('cy', geometry.badgeY);
        badge?.setAttribute('r', geometry.badgeRadius);
        const focus = group.querySelector('.map-node-focus');
        if (focus) {
            focus.setAttribute('cx', geometry.centerX);
            focus.setAttribute('cy', geometry.badgeY);
            focus.setAttribute('r', geometry.badgeRadius + 5);
        }
        group.querySelector('.map-device-icon')?.setAttribute('transform',
            `translate(${geometry.centerX - 24 * geometry.iconScale} ${geometry.badgeY - 24 * geometry.iconScale}) scale(${geometry.iconScale})`);
        const name = group.querySelector('.map-node-name');
        name?.setAttribute('x', geometry.centerX);
        name?.setAttribute('y', geometry.nameY);
        name?.setAttribute('style', `font-size: ${geometry.nameFontSize}px;`);
        const subtitle = group.querySelector('.map-node-ip');
        subtitle?.setAttribute('x', geometry.centerX);
        subtitle?.setAttribute('y', geometry.subtitleY);
        subtitle?.setAttribute('style', `font-size: ${geometry.ipFontSize}px;`);
        const type = group.querySelector('.map-node-type');
        type?.setAttribute('x', geometry.centerX);
        type?.setAttribute('y', geometry.typeY);
        type?.setAttribute('style', `font-size: ${geometry.typeFontSize}px;`);
        const handle = group.querySelector('.map-resize-handle');
        handle?.setAttribute('x', geometry.width - 8);
        handle?.setAttribute('y', geometry.height - 8);
    }

    function updateConnectedLines(movedNode) {
        const nodeId = String(movedNode.id || movedNode.deviceId).toLowerCase();
        const geometry = nodeGeometry(movedNode);
        const centerX = movedNode.x + geometry.centerX;
        const centerY = movedNode.y + geometry.badgeY;

        for (const line of state.connectionsByNodeId.get(nodeId) || []) {
            if (line.dataset.fromId.toLowerCase() === nodeId) {
                line.setAttribute('x1', centerX);
                line.setAttribute('y1', centerY);
            }
            if (line.dataset.toId.toLowerCase() === nodeId) {
                line.setAttribute('x2', centerX);
                line.setAttribute('y2', centerY);
            }
            const label = line.parentElement?.querySelector('.map-connection-label');
            if (label) {
                const x1 = Number(line.getAttribute('x1'));
                const y1 = Number(line.getAttribute('y1'));
                const x2 = Number(line.getAttribute('x2'));
                const y2 = Number(line.getAttribute('y2'));
                label.setAttribute('x', (x1 + x2) / 2);
                label.setAttribute('y', (y1 + y2) / 2 - 8);
            }
        }
    }

    function renderConnectionList() {
        if (!connectionList) return;
        connectionList.replaceChildren();
        document.getElementById('map-connection-count').textContent = `${state.map?.connections?.length || 0}`;
        for (const connection of state.map?.connections || []) {
            const from = connectionNode(connection, true);
            const to = connectionNode(connection, false);
            const item = document.createElement('div');
            item.className = 'map-connection-item';
            const select = el('button', { type: 'button', class: 'map-connection-select' }, `${from ? nodeName(from) : 'Missing'} ↔ ${to ? nodeName(to) : 'Missing'}`);
            select.addEventListener('click', () => selectConnection(connection.id));
            item.append(select);
            item.append(el('small', {}, `${connection.kind} · ${connection.label || 'No label'}`));
            if (canEdit && !state.locked) {
                const remove = el('button', { type: 'button', class: 'map-connection-remove', 'aria-label': 'Remove connection' }, '×');
                remove.addEventListener('click', () => removeConnection(connection.id));
                item.append(remove);
            }
            connectionList.append(item);
        }
        if (!state.map?.connections?.length) connectionList.append(el('p', { class: 'map-list-empty' }, 'No connections on this map.'));
    }

    function labelValue(parent, label, value) {
        const item = document.createElement('div');
        item.className = 'map-detail-item';
        item.append(el('span', {}, label), el('strong', {}, value || '—'));
        parent.append(item);
    }

    function showEmptyInspector() {
        inspector.className = 'map-inspector-empty';
        inspector.textContent = state.toolMode === 'connect' ? (state.connectStartId ? 'Choose another item to finish the connection.' : 'Choose the first item to connect.') : 'Select a device, group, site, connection, or drawing on the canvas.';
    }

    let resizeFrameId = null;

    function appendNodeAppearanceControls(node, id) {
        if (!canEdit || state.locked) return;

        const iconField = el('label', { class: 'map-edit-field map-icon-edit-field' }, 'Map icon');
        const iconSelect = el('select', { class: 'form-select', 'aria-label': 'Map icon' });
        for (const group of ICON_GROUPS) {
            const optionGroup = el('optgroup', { label: group.label });
            for (const value of group.values) {
                const option = el('option', { value }, iconLabel(value));
                if ((node.icon || (node.kind === 'Group' ? 'Group' : node.kind === 'Location' ? 'Site' : 'Device')) === value) option.selected = true;
                optionGroup.append(option);
            }
            iconSelect.append(optionGroup);
        }
        iconSelect.addEventListener('change', event => {
            node.icon = event.target.value;
            markDirty();
            renderNodes();
        });
        iconField.append(iconSelect);

        const sizeField = el('div', { class: 'map-edit-field map-node-size-field' });
        sizeField.append(el('span', {}, 'Icon size'));
        const sizeGroup = el('div', { class: 'map-size-btn-group' });
        const presetButtons = [];
        for (const key of ['sm', 'md', 'lg']) {
            const info = SIZES[key];
            const active = Math.abs(node.width - info.width) < 6 && Math.abs(node.height - info.height) < 6;
            const button = el('button', { type: 'button', class: `map-size-btn ${active ? 'is-active' : ''}` }, info.label);
            button.addEventListener('click', () => {
                node.width = info.width;
                node.height = info.height;
                markDirty();
                updateNodeGeometryElement(node, state.nodeElements.get(String(node.id || node.deviceId).toLowerCase()));
                updateConnectedLines(node);
                showNodeDetails(id);
            });
            sizeGroup.append(button);
            presetButtons.push({ button, info });
        }

        const dimensions = el('small', { class: 'map-size-dimensions', 'aria-live': 'polite' }, `${Math.round(node.width)} × ${Math.round(node.height)}`);
        const slider = el('input', { type: 'range', min: 90, max: 320, step: 5, value: Math.max(90, Math.min(320, Math.round(node.width))), class: 'map-size-range', 'aria-label': 'Resize selected map icon' });
        slider.addEventListener('input', event => {
            node.width = Number(event.target.value);
            node.height = Math.max(70, Math.min(260, Math.round(node.width * 0.8)));
            dimensions.textContent = `${Math.round(node.width)} × ${Math.round(node.height)}`;
            for (const preset of presetButtons) {
                preset.button.classList.toggle('is-active', Math.abs(node.width - preset.info.width) < 6 && Math.abs(node.height - preset.info.height) < 6);
            }
            markDirty();
            if (!resizeFrameId) {
                resizeFrameId = requestAnimationFrame(() => {
                    updateNodeGeometryElement(node, state.nodeElements.get(String(node.id || node.deviceId).toLowerCase()));
                    updateConnectedLines(node);
                    resizeFrameId = null;
                });
            }
        });
        sizeField.append(sizeGroup, slider, dimensions);
        inspector.append(iconField, sizeField);
    }

    function showDeviceDetails(id) {
        const device = state.deviceById.get(id.toLowerCase());
        if (!device) return showEmptyInspector();
        const node = state.map.nodes.find(n => n.kind === 'Device' && String(n.deviceId).toLowerCase() === id.toLowerCase());
        inspector.className = 'map-device-details';
        inspector.replaceChildren();

        const heading = document.createElement('div');
        heading.className = 'map-inspector-device-heading';
        const identity = document.createElement('div');
        identity.append(el('strong', {}, device.name), el('small', { class: 'copyable-ip', 'data-copy-ip': device.ipAddress }, `${device.ipAddress} · ${device.deviceType}`));
        heading.append(el('span', { class: `map-status-mark ${statusClass(device.status)}` }), identity);
        inspector.append(heading);

        if (node) appendNodeAppearanceControls(node, id);

        const statusItem = document.createElement('div');
        statusItem.className = 'map-detail-item';
        statusItem.append(el('span', {}, 'Current status'), el('strong', { class: `device-status status-${String(device.status).toLowerCase()}` }, device.status));
        inspector.append(statusItem);

        labelValue(inspector, 'Response time', device.responseTimeMilliseconds == null ? '—' : `${device.responseTimeMilliseconds} ms`);
        labelValue(inspector, 'Last check', formatDate(device.lastCheckUtc));
        labelValue(inspector, device.status === 'Up' ? 'Uptime' : device.status === 'Down' ? 'Downtime' : 'State duration', durationSince(device.currentStateSinceUtc));
        labelValue(inspector, 'Monitoring', device.monitoringEnabled ? 'Enabled' : 'Paused');

        const location = document.createElement('div');
        location.className = 'map-detail-item';
        location.append(el('span', {}, 'Location'));
        if (device.location && device.locationUrl) {
            const link = el('a', { href: device.locationUrl }, device.location);
            location.append(link);
        } else location.append(el('strong', {}, device.location || '—'));
        inspector.append(location);

        const group = document.createElement('div');
        group.className = 'map-detail-item';
        group.append(el('span', {}, 'Group'));
        if (device.group && device.groupUrl) group.append(el('a', { href: device.groupUrl }, device.group));
        else group.append(el('strong', {}, device.group || '—'));
        inspector.append(group);

        if (device.detailsUrl) {
            const details = el('a', { class: 'map-device-details-link', href: device.detailsUrl }, 'Open device details page →');
            inspector.append(details);
        }

        if (canEdit && !state.locked) {
            const remove = el('button', { type: 'button', class: 'app-danger-button map-remove-detail' }, 'Remove from map');
            remove.addEventListener('click', () => removeNodeFromMap(id));
            inspector.append(remove);
        }
    }

    function showNodeDetails(id) {
        const node = findNode(id);
        if (!node) return showEmptyInspector();
        if (node.kind === 'Device') return showDeviceDetails(node.deviceId);

        const related = relatedDevices(node);
        const upCount = related.filter(d => d.status === 'Up').length;
        const downCount = related.filter(d => d.status === 'Down').length;
        const unknownCount = related.length - upCount - downCount;
        const currentSt = nodeStatus(node);

        inspector.className = 'map-device-details';
        inspector.replaceChildren();

        // Heading
        const heading = document.createElement('div');
        heading.className = 'map-inspector-device-heading';
        const identity = document.createElement('div');
        identity.append(el('strong', {}, nodeName(node)), el('small', {}, node.kind === 'Group' ? 'Operational Group' : 'Site / Building'));
        heading.append(el('span', { class: `map-status-mark ${statusClass(currentSt)}` }), identity);
        inspector.append(heading);

        appendNodeAppearanceControls(node, id);

        // Summary Statistics Pills
        const summaryRow = el('div', { class: 'map-inspector-summary-pills' });
        summaryRow.append(
            el('div', { class: 'map-inspector-pill pill-total' }, `${related.length} Total`),
            el('div', { class: 'map-inspector-pill pill-up' }, `${upCount} UP`),
            el('div', { class: 'map-inspector-pill pill-down' }, `${downCount} DOWN`)
        );
        inspector.append(summaryRow);

        // Device List Section
        const devHeader = el('div', { class: 'map-side-heading', style: 'margin-top: 12px; margin-bottom: 4px;' });
        devHeader.append(el('h3', {}, 'Assigned Devices'), el('span', {}, `${related.length} items`));
        inspector.append(devHeader);

        if (related.length === 0) {
            inspector.append(el('p', { class: 'map-library-hint', style: 'margin-top: 6px;' }, 'No devices are assigned to this building/group in inventory.'));
        } else {
            const list = el('div', { class: 'map-inspector-devices-container' });
            for (const dev of related) {
                const row = el('div', { class: 'map-inspector-device-row' });
                const info = el('div', { class: 'map-inspector-device-info' });
                info.append(
                    el('a', { class: 'map-inspector-device-name', href: dev.detailsUrl || `/Devices/Details/${dev.id}`, title: `View ${dev.name} details` }, dev.name),
                    el('span', { class: 'map-inspector-device-ip copyable-ip', 'data-copy-ip': dev.ipAddress }, `${dev.ipAddress} · ${dev.deviceType}`)
                );
                const badge = el('span', { class: `device-status status-${String(dev.status).toLowerCase()}` });
                badge.append(el('span', { class: 'status-dot' }), document.createTextNode(dev.status));
                row.append(info, badge);
                list.append(row);
            }
            inspector.append(list);
        }

        if (canEdit && !state.locked) {
            const remove = el('button', { type: 'button', class: 'app-danger-button map-remove-detail' }, 'Remove from map');
            remove.addEventListener('click', () => removeNodeFromMap(id));
            inspector.append(remove);
        }
    }

    const viewModePopup = document.getElementById('map-viewmode-popup');
    const popupTitle = document.getElementById('popup-title');
    const popupSubtitle = document.getElementById('popup-subtitle');
    const popupStatusMark = document.getElementById('popup-status-mark');
    const popupStatTotal = document.getElementById('popup-stat-total');
    const popupStatUp = document.getElementById('popup-stat-up');
    const popupStatDown = document.getElementById('popup-stat-down');
    const popupDevicesList = document.getElementById('popup-devices-list');
    const popupStatsContainer = document.getElementById('popup-stats');

    function hideViewModePopup() {
        if (viewModePopup) viewModePopup.hidden = true;
    }

    function showViewModePopup(node) {
        if (!viewModePopup) return;
        const currentSt = nodeStatus(node);
        const stClass = statusClass(currentSt);

        if (popupStatusMark) popupStatusMark.className = `map-status-mark ${stClass}`;
        if (popupTitle) popupTitle.textContent = nodeName(node);

        if (node.kind === 'Device') {
            const dev = state.deviceById.get(String(node.deviceId || '').toLowerCase());
            if (popupSubtitle) popupSubtitle.textContent = `Device · ${dev?.deviceType || 'Network Host'}`;
            if (popupStatsContainer) popupStatsContainer.hidden = true;
            if (popupDevicesList) {
                popupDevicesList.replaceChildren();
                const card = el('div', { class: 'map-popup-single-device' });
                card.innerHTML = `
                    <div class="map-popup-detail-row"><span>IP Address</span><strong class="copyable-ip" data-copy-ip="${dev?.ipAddress || ''}">${dev?.ipAddress || '—'}</strong></div>
                    <div class="map-popup-detail-row"><span>Status</span><span class="device-status status-${stClass}"><span class="status-dot"></span>${currentSt}</span></div>
                    <div class="map-popup-detail-row"><span>Type</span><strong>${dev?.deviceType || '—'}</strong></div>
                    <div class="map-popup-detail-row"><span>Location</span><strong>${dev?.location || '—'}</strong></div>
                    <div class="map-popup-detail-row"><span>Building</span><strong>${dev?.building || '—'}</strong></div>
                    <div class="map-popup-detail-row"><span>Group</span><strong>${dev?.group || '—'}</strong></div>
                    <div class="map-popup-detail-row"><span>Monitoring</span><strong>${dev?.monitoringMethod || 'ICMP Ping'}</strong></div>
                    <a href="${dev?.detailsUrl || `/Devices/Details/${dev?.id}`}" class="app-primary-button map-popup-view-btn" target="_blank">Open device details →</a>
                `;
                popupDevicesList.append(card);
            }
            viewModePopup.hidden = false;
            return;
        }

        // For Groups and Sites / Buildings
        if (popupSubtitle) popupSubtitle.textContent = node.kind === 'Group' ? 'Operational Group' : 'Site / Building';
        if (popupStatsContainer) popupStatsContainer.hidden = false;

        const related = relatedDevices(node);
        const upCount = related.filter(d => d.status === 'Up').length;
        const downCount = related.filter(d => d.status === 'Down').length;

        if (popupStatTotal) popupStatTotal.textContent = `${related.length} Total Hosts`;
        if (popupStatUp) popupStatUp.textContent = `${upCount} UP`;
        if (popupStatDown) popupStatDown.textContent = `${downCount} DOWN`;

        if (popupDevicesList) {
            popupDevicesList.replaceChildren();
            if (related.length === 0) {
                const empty = el('p', { class: 'map-popup-empty' }, 'No devices assigned to this group or site in inventory.');
                popupDevicesList.append(empty);
            } else {
                for (const dev of related) {
                    const row = el('div', {
                        class: 'map-popup-device-item',
                    });
                    const devStClass = statusClass(dev.status);
                    const left = el('div', { class: 'map-popup-dev-left' });
                    const text = el('div', { class: 'map-popup-dev-text' });
                    text.append(
                        el('a', { class: 'map-popup-dev-name', href: dev.detailsUrl || `/Devices/Details/${dev.id}`, title: `Open ${dev.name} details in inventory`, target: '_blank' }, dev.name),
                        el('span', { class: 'map-popup-dev-meta copyable-ip', 'data-copy-ip': dev.ipAddress }, `${dev.ipAddress} · ${dev.deviceType}`)
                    );
                    left.append(el('span', { class: `map-status-mark ${devStClass}` }), text);
                    const status = el('span', { class: `device-status status-${devStClass}` });
                    status.append(el('span', { class: 'status-dot' }), document.createTextNode(dev.status));
                    row.append(left, status);
                    popupDevicesList.append(row);
                }
            }
        }

        viewModePopup.hidden = false;
    }

    function toggleViewMode(forced) {
        state.viewMode = typeof forced === 'boolean' ? forced : !state.viewMode;
        root.classList.toggle('is-view-mode', state.viewMode);

        const modeIcon = document.getElementById('map-mode-icon');
        const modeText = document.getElementById('map-mode-text');
        const quickBtn = document.getElementById('map-quick-viewmode');

        if (state.viewMode) {
            if (modeIcon) modeIcon.textContent = '✏️';
            if (modeText) modeText.textContent = 'Edit mode';
            if (quickBtn) quickBtn.textContent = 'Edit mode';
            notify('View mode enabled. Only map is visible. Click any group or site to inspect hosts.');
            setTimeout(() => { fitMap(); }, 80);
            if (state.selectedNodeId) {
                const node = findNode(state.selectedNodeId);
                if (node) showViewModePopup(node);
            }
        } else {
            if (modeIcon) modeIcon.textContent = '👁️';
            if (modeText) modeText.textContent = 'View mode';
            if (quickBtn) quickBtn.textContent = 'View only';
            hideViewModePopup();
            notify('Edit mode enabled.');
        }
    }

    function showConnectionDetails(id) {
        const connection = state.map.connections.find(item => item.id.toLowerCase() === id.toLowerCase());
        if (!connection) return showEmptyInspector();
        const from = connectionNode(connection, true);
        const to = connectionNode(connection, false);
        document.getElementById('map-line-width').value = String(connection.strokeWidth || 2.5);
        inspector.className = 'map-device-details';
        inspector.replaceChildren();
        inspector.append(el('h3', {}, 'Connection details'));
        labelValue(inspector, 'From', from ? nodeName(from) : 'Missing item');
        labelValue(inspector, 'To', to ? nodeName(to) : 'Missing item');
        if (!canEdit || state.locked) {
            labelValue(inspector, 'Type', connection.kind);
            labelValue(inspector, 'Line width', `${connection.strokeWidth || 2.5}px`);
            return;
        }

        const kindLabel = el('label', { class: 'map-edit-field' }, 'Connection type');
        const kind = el('select', { class: 'form-select' });
        ['Ethernet', 'Fiber', 'Wireless', 'Logical'].forEach(k => {
            const opt = el('option', { value: k }, k);
            if (connection.kind === k) opt.selected = true;
            kind.append(opt);
        });
        kind.addEventListener('change', e => {
            connection.kind = e.target.value;
            renderConnections();
            renderConnectionList();
            markDirty();
        });
        kindLabel.append(kind);

        const widthLabel = el('label', { class: 'map-edit-field' }, 'Line width');
        const width = el('select', { class: 'form-select' });
        [['1', 'Thin'], ['2', 'Light'], ['2.5', 'Normal'], ['3', 'Medium'], ['5', 'Thick'], ['8', 'Heavy']].forEach(([val, lbl]) => {
            const opt = el('option', { value: val }, lbl);
            if (String(connection.strokeWidth || 2.5) === val) opt.selected = true;
            width.append(opt);
        });
        width.addEventListener('change', e => {
            connection.strokeWidth = Number(e.target.value);
            renderConnections();
            markDirty();
        });
        widthLabel.append(width);

        const label = el('label', { class: 'map-edit-field' }, 'Connection label');
        const input = el('input', { type: 'text', class: 'form-control', value: connection.label || '', placeholder: 'e.g. 10G Trunk uplink' });
        input.addEventListener('input', e => {
            connection.label = e.target.value;
            renderConnections();
            renderConnectionList();
            markDirty();
        });
        label.append(input);

        inspector.append(kindLabel, widthLabel, label);

        const remove = el('button', { type: 'button', class: 'app-danger-button map-remove-detail' }, 'Remove connection');
        remove.addEventListener('click', () => removeConnection(id));
        inspector.append(remove);
    }

    function showAnnotationDetails(id) {
        const annotation = state.map.annotations.find(item => item.id.toLowerCase() === id.toLowerCase());
        if (!annotation) return showEmptyInspector();
        document.getElementById('map-line-width').value = String(annotation.strokeWidth || 2);
        document.getElementById('map-stroke-color').value = annotation.strokeColor || '#64748b';
        document.getElementById('map-fill-color').value = annotation.fillColor || '#93c5fd';
        document.getElementById('map-no-fill').checked = !annotation.fillColor;
        inspector.className = 'map-device-details';
        inspector.replaceChildren();
        inspector.append(el('h3', {}, `${annotation.kind} drawing`));
        if (canEdit && !state.locked && annotation.kind === 'Text') {
            const label = el('label', { class: 'map-edit-field' }, 'Text content');
            const input = el('input', { type: 'text', class: 'form-control', value: annotation.text || '', placeholder: 'Label text' });
            input.addEventListener('input', e => {
                annotation.text = e.target.value;
                renderAnnotations();
                markDirty();
            });
            label.append(input);
            inspector.append(label);
        } else if (annotation.kind === 'Text') labelValue(inspector, 'Text', annotation.text);
        labelValue(inspector, 'Stroke', annotation.strokeColor);
        labelValue(inspector, 'Fill', annotation.fillColor || 'None');

        if (canEdit && !state.locked) {
            const remove = el('button', { type: 'button', class: 'app-danger-button map-remove-detail' }, 'Delete drawing');
            remove.addEventListener('click', () => removeAnnotation(id));
            inspector.append(remove);
        }
    }

    function selectNode(id) {
        if (state.toolMode === 'connect') {
            if (!state.connectStartId) {
                state.connectStartId = id;
                notify('Choose another device, group, or site to connect.');
                renderNodes();
                showEmptyInspector();
                return;
            }
            if (state.connectStartId.toLowerCase() === id.toLowerCase()) {
                state.connectStartId = null;
                renderNodes();
                showEmptyInspector();
                return;
            }
            addConnection(state.connectStartId, id);
            state.connectStartId = null;
            return;
        }

        state.selectedNodeId = id;
        const node = findNode(id);
        state.selectedDeviceId = node?.kind === 'Device' ? node.deviceId : null;
        state.selectedConnectionId = null;
        state.selectedAnnotationId = null;
        renderNodes();
        renderConnections();
        renderAnnotations();
        showNodeDetails(id);
        if (state.viewMode && node) {
            showViewModePopup(node);
        }
    }

    function selectConnection(id) {
        state.selectedConnectionId = id;
        state.selectedNodeId = null;
        state.selectedDeviceId = null;
        state.selectedAnnotationId = null;
        renderConnections();
        renderNodes();
        renderAnnotations();
        showConnectionDetails(id);
    }

    function selectAnnotation(id) {
        state.selectedAnnotationId = id;
        state.selectedNodeId = null;
        state.selectedDeviceId = null;
        state.selectedConnectionId = null;
        renderAnnotations();
        renderNodes();
        renderConnections();
        showAnnotationDetails(id);
    }

    function beginAnnotationDrawing(event, kind) {
        if (!canEdit || state.locked) return;
        const point = svgPoint(event);
        const stroke = document.getElementById('map-stroke-color').value;
        const fill = document.getElementById('map-no-fill').checked ? null : document.getElementById('map-fill-color').value;
        const width = Number(document.getElementById('map-line-width').value) || 2;
        const annotation = {
            id: crypto.randomUUID(),
            kind,
            x: snap(point.x),
            y: snap(point.y),
            width: kind === 'Text' ? 140 : 0,
            height: kind === 'Text' ? 30 : 0,
            strokeColor: stroke,
            fillColor: fill,
            strokeWidth: width,
            text: kind === 'Text' ? 'Label' : null
        };
        state.map.annotations ||= [];
        state.map.annotations.push(annotation);
        state.selectedAnnotationId = annotation.id;
        state.selectedNodeId = null;
        state.selectedDeviceId = null;
        state.selectedConnectionId = null;
        if (kind === 'Text') {
            renderAnnotations();
            showAnnotationDetails(annotation.id);
            markDirty();
            return;
        }
        state.pointer = { mode: 'draw', annotation, startX: snap(point.x), startY: snap(point.y) };
        canvas.setPointerCapture?.(event.pointerId);
    }

    function beginAnnotationPointer(event, annotation) {
        if (event.button !== 0) return;
        event.stopPropagation();
        selectAnnotation(annotation.id);
        if (!canEdit || state.locked || state.toolMode !== 'select') return;
        const point = svgPoint(event);
        state.pointer = {
            mode: 'annotation',
            annotation,
            startX: point.x,
            startY: point.y,
            x: annotation.x,
            y: annotation.y,
            moved: false
        };
        canvas.setPointerCapture?.(event.pointerId);
    }

    function addDeviceToMap(id, x, y) {
        if (!canEdit || state.locked) return;
        if (state.map.nodes.some(node => node.kind === 'Device' && node.deviceId.toLowerCase() === id.toLowerCase())) return;
        
        const device = state.deviceById.get(id.toLowerCase());
        const selectedIcon = document.getElementById('map-icon-kind').value || 'Auto';
        const icon = selectedIcon === 'Auto' ? getDefaultIconForDeviceType(device?.deviceType) : selectedIcon;
        const sizeInfo = SIZES[currentDefaultSize] || SIZES.md;
        
        state.map.nodes.push({ id, kind: 'Device', deviceId: id, icon, x, y, width: sizeInfo.width, height: sizeInfo.height });
        state.selectedNodeId = id;
        state.selectedDeviceId = id;
        state.selectedConnectionId = null;
        state.selectedAnnotationId = null;
        markDirty();
        render();
    }

    function addEntityToMap(item, x, y) {
        if (!canEdit || state.locked) return;
        const alreadyPlaced = state.map.nodes.some(node => node.kind === item.kind &&
            String(node.groupId || node.locationId).toLowerCase() === item.id.toLowerCase());
        if (alreadyPlaced) return;
        
        const sizeInfo = SIZES[currentDefaultSize] || SIZES.md;
        const node = {
            id: crypto.randomUUID(),
            kind: item.kind,
            deviceId: '00000000-0000-0000-0000-000000000000',
            groupId: item.kind === 'Group' ? item.id : null,
            locationId: item.kind === 'Location' ? item.id : null,
            icon: item.kind === 'Group' ? 'Group' : 'Building',
            x, y, width: sizeInfo.width, height: sizeInfo.height
        };
        state.map.nodes.push(node);
        state.selectedNodeId = node.id;
        state.selectedDeviceId = null;
        state.selectedConnectionId = null;
        state.selectedAnnotationId = null;
        markDirty();
        render();
    }

    function addConnection(fromNodeId, toNodeId) {
        if (!canEdit || state.locked) return;
        const fromNode = findNode(fromNodeId);
        const toNode = findNode(toNodeId);
        if (!fromNode || !toNode) return;
        const duplicate = state.map.connections.some(connection =>
            (String(connectionNode(connection, true)?.id || connection.fromDeviceId).toLowerCase() === fromNodeId.toLowerCase() && String(connectionNode(connection, false)?.id || connection.toDeviceId).toLowerCase() === toNodeId.toLowerCase()) ||
            (String(connectionNode(connection, true)?.id || connection.fromDeviceId).toLowerCase() === toNodeId.toLowerCase() && String(connectionNode(connection, false)?.id || connection.toDeviceId).toLowerCase() === fromNodeId.toLowerCase()));
        if (duplicate) { notify('Those items are already connected.', true); render(); return; }
        const emptyGuid = '00000000-0000-0000-0000-000000000000';
        state.map.connections.push({
            id: crypto.randomUUID(),
            fromNodeId,
            toNodeId,
            fromDeviceId: fromNode.kind === 'Device' ? fromNode.deviceId : emptyGuid,
            toDeviceId: toNode.kind === 'Device' ? toNode.deviceId : emptyGuid,
            kind: 'Ethernet',
            strokeWidth: Number(document.getElementById('map-line-width').value) || 2.5,
            label: ''
        });
        state.selectedConnectionId = state.map.connections[state.map.connections.length - 1].id;
        state.selectedDeviceId = null;
        state.selectedNodeId = null;
        state.selectedAnnotationId = null;
        markDirty();
        notify('Connection added. Select it to set its type, width, or label.');
        render();
    }

    function removeConnection(id) {
        if (!canEdit || state.locked) return;
        state.map.connections = state.map.connections.filter(connection => connection.id.toLowerCase() !== id.toLowerCase());
        state.selectedConnectionId = null;
        markDirty();
        render();
    }

    function removeNodeFromMap(id) {
        if (!canEdit || state.locked) return;
        const node = findNode(id);
        if (!node) return;
        const endpointId = String(node.id || node.deviceId).toLowerCase();
        const deviceId = node.kind === 'Device' ? node.deviceId.toLowerCase() : null;
        state.map.nodes = state.map.nodes.filter(item => String(item.id || item.deviceId).toLowerCase() !== endpointId);
        state.map.connections = state.map.connections.filter(connection => {
            const endpointMatch = String(connection.fromNodeId || connection.fromDeviceId).toLowerCase() === endpointId ||
                String(connection.toNodeId || connection.toDeviceId).toLowerCase() === endpointId;
            const legacyDeviceMatch = deviceId &&
                (connection.fromDeviceId.toLowerCase() === deviceId || connection.toDeviceId.toLowerCase() === deviceId);
            return !endpointMatch && !legacyDeviceMatch;
        });
        state.selectedNodeId = null;
        state.selectedDeviceId = null;
        state.selectedConnectionId = null;
        state.selectedAnnotationId = null;
        state.connectStartId = null;
        markDirty();
        render();
    }

    function removeAnnotation(id) {
        if (!canEdit || state.locked) return;
        state.map.annotations = state.map.annotations.filter(annotation => annotation.id.toLowerCase() !== id.toLowerCase());
        state.selectedAnnotationId = null;
        markDirty();
        render();
    }

    function applyViewBox() {
        const width = MAP_WIDTH / state.zoom;
        const height = MAP_HEIGHT / state.zoom;
        canvas.setAttribute('viewBox', `${state.panX} ${state.panY} ${width} ${height}`);
        document.getElementById('map-zoom-value').textContent = `${Math.round(state.zoom * 100)}%`;
    }

    function snap(value) {
        return document.getElementById('map-snap-grid')?.checked ? Math.round(value / 24) * 24 : value;
    }

    function setToolMode(mode) {
        state.toolMode = mode;
        state.connectStartId = null;
        document.getElementById('map-tool-mode').value = mode;
        const hints = {
            select: 'Drag items to arrange them. Drag empty space to pan.',
            connect: 'Select two devices, groups, or sites to connect them.',
            line: 'Drag on the canvas to draw a line.',
            rectangle: 'Drag on the canvas to draw a rectangle.',
            ellipse: 'Drag on the canvas to draw an ellipse.',
            text: 'Click the canvas to place a text label.'
        };
        document.getElementById('map-hint').textContent = state.locked ? 'Map locked. Unlock to move, draw, pan, or zoom.' : hints[mode];
        if (state.map) renderNodes();
        showEmptyInspector();
    }

    function updateLockState() {
        const button = document.getElementById('map-lock');
        button.textContent = state.locked ? 'Unlock map' : 'Lock map';
        button.setAttribute('aria-pressed', String(state.locked));
        button.classList.toggle('is-active', state.locked);
        root.classList.toggle('map-is-locked', state.locked);
        const editControlIds = ['map-name', 'map-location', 'map-building', 'map-icon-kind', 'map-tool-mode', 'map-line-width', 'map-stroke-color', 'map-fill-color', 'map-no-fill', 'map-snap-grid', 'map-import-file', 'map-arrange', 'map-delete'];
        for (const id of editControlIds) {
            const control = document.getElementById(id);
            if (control) control.disabled = !canEdit || state.locked;
        }
        if (!state.map) return;
        renderDevices();
        renderEntities();
        renderConnectionList();
        renderNodes();
        if (state.selectedNodeId) showNodeDetails(state.selectedNodeId);
        else if (state.selectedConnectionId) showConnectionDetails(state.selectedConnectionId);
        else if (state.selectedAnnotationId) showAnnotationDetails(state.selectedAnnotationId);
    }

    function updateSelectedStyle(property, value) {
        if (!canEdit || state.locked || !state.map) return;
        const annotation = state.map.annotations.find(item => item.id === state.selectedAnnotationId);
        const connection = state.map.connections.find(item => item.id === state.selectedConnectionId);
        if (annotation) {
            if (property === 'fillColor' && document.getElementById('map-no-fill').checked) annotation.fillColor = null;
            else annotation[property] = value;
            renderAnnotations();
            showAnnotationDetails(annotation.id);
            markDirty();
        } else if (connection && property === 'strokeWidth') {
            connection.strokeWidth = Number(value);
            renderConnections();
            renderConnectionList();
            showConnectionDetails(connection.id);
            markDirty();
        }
    }

    function fitMap() {
        const nodeBounds = state.map.nodes.map(node => ({ x: node.x, y: node.y, width: Number(node.width) || 160, height: Number(node.height) || 128 }));
        const drawingBounds = (state.map.annotations || []).map(annotation => ({
            x: Math.min(annotation.x, annotation.x + annotation.width),
            y: Math.min(annotation.y, annotation.y + annotation.height),
            width: Math.abs(annotation.width) || 40,
            height: Math.abs(annotation.height) || 40
        }));
        const bounds = [...nodeBounds, ...drawingBounds];
        if (!bounds.length) { state.panX = 0; state.panY = 0; state.zoom = 1; applyViewBox(); return; }
        const left = Math.min(...bounds.map(item => item.x)) - 80;
        const top = Math.min(...bounds.map(item => item.y)) - 80;
        const right = Math.max(...bounds.map(item => item.x + item.width)) + 80;
        const bottom = Math.max(...bounds.map(item => item.y + item.height)) + 80;
        const width = Math.max(480, right - left);
        const height = Math.max(360, bottom - top);
        state.zoom = Math.max(0.4, Math.min(2.5, Math.min(MAP_WIDTH / width, MAP_HEIGHT / height)));
        state.panX = left;
        state.panY = top;
        applyViewBox();
    }

    function svgPoint(event) {
        const point = canvas.createSVGPoint();
        point.x = event.clientX;
        point.y = event.clientY;
        return point.matrixTransform(canvas.getScreenCTM().inverse());
    }

    function beginNodePointer(event, node, group) {
        if (event.button !== 0) return;
        event.stopPropagation();
        if (state.toolMode === 'connect') return;
        if (state.toolMode !== 'select') {
            if (!state.locked) beginAnnotationDrawing(event, state.toolMode === 'text' ? 'Text' : state.toolMode === 'line' ? 'Line' : state.toolMode === 'rectangle' ? 'Rectangle' : 'Ellipse');
            return;
        }
        selectNode(node.id || node.deviceId);
        if (state.locked || !canEdit) return;
        const activeGroup = state.nodeElements.get(String(node.id || node.deviceId).toLowerCase()) || group;
        const point = svgPoint(event);
        const isResize = event.target?.dataset?.resize === 'true';
        state.pointer = {
            mode: isResize ? 'resize' : 'node',
            node,
            group: activeGroup,
            startId: node.id || node.deviceId,
            startX: point.x,
            startY: point.y,
            nodeX: node.x,
            nodeY: node.y,
            width: node.width,
            height: node.height,
            moved: false
        };
        canvas.setPointerCapture?.(event.pointerId);
    }

    canvas.addEventListener('pointerdown', event => {
        if (event.target.closest?.('.map-node') || event.target.closest?.('.map-connection') || event.target.closest?.('.map-annotation')) return;
        if (event.button !== 0 || state.locked) return;
        if (state.toolMode === 'connect') return;
        if (state.toolMode !== 'select') {
            beginAnnotationDrawing(event, state.toolMode === 'text' ? 'Text' : state.toolMode === 'line' ? 'Line' : state.toolMode === 'rectangle' ? 'Rectangle' : 'Ellipse');
            return;
        }
        state.pointer = { mode: 'pan', startX: event.clientX, startY: event.clientY, panX: state.panX, panY: state.panY, zoom: state.zoom };
        canvas.setPointerCapture?.(event.pointerId);
    });

    let animationFrameId = null;

    canvas.addEventListener('pointermove', event => {
        if (!state.pointer) return;
        if (state.locked) { state.pointer = null; return; }
        const drag = state.pointer;
        const point = svgPoint(event);

        if (drag.mode === 'node') {
            drag.moved = true;
            // Expansive coordinates: allows placement anywhere in wide workspace (-2000 to 6000)
            const newX = snap(drag.nodeX + point.x - drag.startX);
            const newY = snap(drag.nodeY + point.y - drag.startY);
            drag.node.x = newX;
            drag.node.y = newY;

            // Fluid 60fps hardware accelerated updates
            if (drag.group) {
                drag.group.setAttribute('transform', `translate(${newX} ${newY})`);
            }
            updateConnectedLines(drag.node);
        } else if (drag.mode === 'resize') {
            drag.moved = true;
            drag.node.width = Math.max(90, Math.min(320, drag.width + point.x - drag.startX));
            drag.node.height = Math.max(70, Math.min(260, drag.height + point.y - drag.startY));
            if (!animationFrameId) {
                animationFrameId = requestAnimationFrame(() => {
                    updateNodeGeometryElement(drag.node, drag.group);
                    updateConnectedLines(drag.node);
                    animationFrameId = null;
                });
            }
        } else if (drag.mode === 'annotation') {
            drag.moved = true;
            drag.annotation.x = snap(drag.x + point.x - drag.startX);
            drag.annotation.y = snap(drag.y + point.y - drag.startY);
            if (!animationFrameId) {
                animationFrameId = requestAnimationFrame(() => {
                    renderAnnotations();
                    animationFrameId = null;
                });
            }
        } else if (drag.mode === 'draw') {
            if (drag.annotation.kind === 'Line') {
                drag.annotation.width = point.x - drag.startX;
                drag.annotation.height = point.y - drag.startY;
            } else {
                drag.annotation.x = Math.min(drag.startX, point.x);
                drag.annotation.y = Math.min(drag.startY, point.y);
                drag.annotation.width = Math.abs(point.x - drag.startX);
                drag.annotation.height = Math.abs(point.y - drag.startY);
            }
            if (!animationFrameId) {
                animationFrameId = requestAnimationFrame(() => {
                    renderAnnotations();
                    animationFrameId = null;
                });
            }
        } else if (drag.mode === 'pan') {
            const bounds = canvas.getBoundingClientRect();
            state.panX = drag.panX - (event.clientX - drag.startX) * (MAP_WIDTH / drag.zoom) / bounds.width;
            state.panY = drag.panY - (event.clientY - drag.startY) * (MAP_HEIGHT / drag.zoom) / bounds.height;
            applyViewBox();
        }
    });

    canvas.addEventListener('pointerup', () => {
        if (!state.pointer) return;
        if (state.pointer.mode === 'node' && state.pointer.moved) {
            markDirty();
            renderConnections();
            renderNodes();
        } else if (state.pointer.mode === 'draw') {
            const annotation = state.pointer.annotation;
            if (Math.abs(annotation.width) < 8 && Math.abs(annotation.height) < 8) {
                state.map.annotations = state.map.annotations.filter(item => item.id !== annotation.id);
                state.selectedAnnotationId = null;
                renderAnnotations();
                showEmptyInspector();
            } else {
                markDirty();
                renderAnnotations();
                showAnnotationDetails(annotation.id);
            }
        } else if (state.pointer.moved) {
            markDirty();
        }
        state.pointer = null;
    });

    canvas.addEventListener('pointercancel', () => { state.pointer = null; });
    canvas.addEventListener('dragover', event => { event.preventDefault(); });
    
    canvas.addEventListener('drop', event => {
        event.preventDefault();
        if (!canEdit || state.locked || !state.map) return;
        const deviceId = event.dataTransfer.getData('text/device-id');
        const entityJson = event.dataTransfer.getData('text/entity-json');
        const point = svgPoint(event);
        
        if (deviceId) {
            addDeviceToMap(deviceId, snap(point.x - 80), snap(point.y - 50));
        } else if (entityJson) {
            try {
                const entity = JSON.parse(entityJson);
                addEntityToMap(entity, snap(point.x - 80), snap(point.y - 50));
            } catch {}
        }
    });

    canvas.addEventListener('click', event => {
        if (event.target.id === 'map-canvas' || event.target.classList.contains('map-grid-background')) {
            state.selectedNodeId = null;
            state.selectedDeviceId = null;
            state.selectedConnectionId = null;
            state.selectedAnnotationId = null;
            renderNodes();
            renderAnnotations();
            renderConnections();
            showEmptyInspector();
            hideViewModePopup();
        }
    });

    canvas.addEventListener('wheel', event => {
        event.preventDefault();
        if (state.locked) return;
        state.zoom = Math.max(0.35, Math.min(2.75, state.zoom + (event.deltaY < 0 ? 0.12 : -0.12)));
        applyViewBox();
    }, { passive: false });

    // Size Picker Toolbar Events
    ['sm', 'md', 'lg'].forEach(sz => {
        const btn = document.getElementById(`map-size-btn-${sz}`);
        if (!btn) return;
        btn.addEventListener('click', () => {
            currentDefaultSize = sz;
            document.querySelectorAll('.map-size-pill').forEach(b => b.classList.remove('is-active'));
            btn.classList.add('is-active');

            // If a node is currently selected, resize it too
            if (state.selectedNodeId) {
                const node = findNode(state.selectedNodeId);
                if (node) {
                    const target = SIZES[sz];
                    node.width = target.width;
                    node.height = target.height;
                    markDirty();
                    updateNodeGeometryElement(node, state.nodeElements.get(String(node.id || node.deviceId).toLowerCase()));
                    updateConnectedLines(node);
                    showNodeDetails(state.selectedNodeId);
                }
            }
        });
    });

    document.getElementById('map-device-search').addEventListener('input', renderDevices);
    document.getElementById('map-entity-search').addEventListener('input', renderEntities);
    document.getElementById('map-location').addEventListener('change', event => { state.map.locationId = event.target.value || null; markDirty(); });
    document.getElementById('map-building').addEventListener('change', event => { state.map.buildingId = event.target.value || null; markDirty(); });
    nameInput.addEventListener('input', () => { if (state.map) { state.map.name = nameInput.value; document.getElementById('map-title').textContent = nameInput.value || 'New network map'; markDirty(); } });
    document.getElementById('map-tool-mode').addEventListener('change', event => setToolMode(event.target.value));
    document.getElementById('map-line-width').addEventListener('change', event => updateSelectedStyle('strokeWidth', event.target.value));
    document.getElementById('map-stroke-color').addEventListener('change', event => updateSelectedStyle('strokeColor', event.target.value));
    document.getElementById('map-fill-color').addEventListener('change', event => updateSelectedStyle('fillColor', event.target.value));
    document.getElementById('map-no-fill').addEventListener('change', event => {
        if (state.selectedAnnotationId) updateSelectedStyle('fillColor', event.target.checked ? null : document.getElementById('map-fill-color').value);
    });
    document.getElementById('map-lock').addEventListener('click', () => { state.locked = !state.locked; state.pointer = null; updateLockState(); });
    document.getElementById('map-arrange').addEventListener('click', () => {
        if (!canEdit || state.locked || !state.map.nodes.length) return;
        const sizeInfo = SIZES[currentDefaultSize] || SIZES.md;
        const ordered = [...state.map.nodes].sort((left, right) => left.y - right.y || left.x - right.x);
        ordered.forEach((node, index) => {
            node.width = sizeInfo.width;
            node.height = sizeInfo.height;
            node.x = 80 + (index % 5) * (sizeInfo.width + 120);
            node.y = 80 + Math.floor(index / 5) * (sizeInfo.height + 60);
        });
        markDirty();
        render();
        fitMap();
    });
    document.getElementById('map-zoom-in').addEventListener('click', () => { if (!state.locked) { state.zoom = Math.min(2.75, state.zoom + 0.15); applyViewBox(); } });
    document.getElementById('map-zoom-out').addEventListener('click', () => { if (!state.locked) { state.zoom = Math.max(0.35, state.zoom - 0.15); applyViewBox(); } });
    document.getElementById('map-zoom-reset').addEventListener('click', () => { if (!state.locked) fitMap(); });
    document.getElementById('map-fullscreen').addEventListener('click', async () => {
        if (document.fullscreenElement === root) await document.exitFullscreen?.();
        else await root.requestFullscreen?.();
    });
    document.addEventListener('fullscreenchange', () => {
        document.getElementById('map-fullscreen').textContent = document.fullscreenElement === root ? 'Exit full screen' : 'Full screen';
    });
    document.getElementById('map-save').addEventListener('click', async () => {
        if (!canEdit || !state.map) return;
        state.map.name = nameInput.value.trim();
        try {
            const result = await requestJson(root.dataset.saveUrl, { method: 'POST', body: JSON.stringify(state.map) });
            if (!result.success) throw new Error(result.error || 'The map could not be saved.');
            state.map.id = result.mapId;
            state.dirty = false;
            saveButton.textContent = 'Saved';
            saveButton.disabled = true;
            document.getElementById('map-export').disabled = false;
            history.replaceState(null, '', `${location.pathname}?id=${encodeURIComponent(result.mapId)}`);
            root.dataset.mapId = result.mapId;
            notify('Map saved.');
        } catch (error) { notify(error.message, true); }
    });
    document.getElementById('map-export').addEventListener('click', () => {
        if (hasSavedMap()) window.location.href = `/NetworkMaps/Export/${encodeURIComponent(state.map.id)}`;
    });
    document.getElementById('map-delete')?.addEventListener('click', async () => {
        if (!state.map?.id || !confirm(`Delete “${state.map.name}”?`)) return;
        try {
            await requestJson(`${root.dataset.deleteUrl}?id=${encodeURIComponent(state.map.id)}`, { method: 'POST', body: '{}' });
            window.location.href = '/NetworkMaps';
        } catch (error) { notify(error.message, true); }
    });

    const importFile = document.getElementById('map-import-file');
    importFile.addEventListener('change', async () => {
        if (!canEdit || state.locked) { importFile.value = ''; return; }
        const file = importFile.files?.[0];
        if (!file) return;
        try {
            const json = await file.text();
            const preview = await requestJson(root.dataset.importPreviewUrl, { method: 'POST', body: JSON.stringify({ json }) });
            state.importJson = json;
            document.getElementById('map-import-summary').textContent = preview.mapName ? `Previewing “${preview.mapName}”.` : 'Previewing imported map.';
            document.getElementById('map-import-name').value = preview.mapName ? `${preview.mapName} (copy)` : '';
            const missing = document.getElementById('map-import-missing');
            missing.replaceChildren();
            for (const id of preview.missingDeviceIds || []) missing.append(el('div', { class: 'map-missing-reference' }, `Missing device ID: ${id}`));
            if (preview.error) missing.prepend(el('p', { class: 'validation-message' }, preview.error));
            document.getElementById('map-import-confirm').disabled = !preview.success;
            importDialog.showModal();
        } catch (error) { notify(error.message, true); }
        finally { importFile.value = ''; }
    });
    document.getElementById('map-import-confirm').addEventListener('click', async () => {
        if (!state.importJson) return;
        try {
            const result = await requestJson(root.dataset.importUrl, { method: 'POST', body: JSON.stringify({ json: state.importJson, name: document.getElementById('map-import-name').value }) });
            if (!result.success) throw new Error(result.error || 'The map could not be imported.');
            window.location.href = `/NetworkMaps/Editor/${encodeURIComponent(result.mapId)}`;
        } catch (error) { notify(error.message, true); importDialog.close(); }
    });

    document.getElementById('map-mode-toggle')?.addEventListener('click', () => toggleViewMode());
    document.getElementById('map-quick-viewmode')?.addEventListener('click', () => toggleViewMode());
    document.getElementById('popup-close-btn')?.addEventListener('click', hideViewModePopup);

    initialize();
    window.setInterval(updateLiveStatus, 10000);
})();
