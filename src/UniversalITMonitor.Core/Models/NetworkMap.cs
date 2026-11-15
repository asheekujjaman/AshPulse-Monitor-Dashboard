namespace UniversalITMonitor.Core.Models;

public enum MapIconKind
{
    Router,
    Switch,
    Firewall,
    Server,
    AccessPoint,
    Cloud,
    Internet,
    Pc,
    Printer,
    Storage,
    Ups,
    Device,
    CoreSwitch,
    DistributionSwitch,
    AccessSwitch,
    WirelessAccessPoint,
    Building,
    Group,
    Site,
    Laptop,
    MobilePhone,
    Tablet,
    VoipPhone,
    Camera,
    Nvr,
    Database,
    Backup,
    LoadBalancer,
    Modem,
    Gateway,
    Vpn,
    VirtualMachine,
    Hypervisor,
    Rack,
    Monitor,
    PowerDistributionUnit,
    Fiber,
    Subnet,
    SecuritySensor
}

public enum MapConnectionKind
{
    Ethernet,
    Fiber,
    Wireless,
    Logical
}

public enum NetworkMapNodeKind
{
    Device,
    Group,
    Location
}

public enum NetworkMapAnnotationKind
{
    Line,
    Rectangle,
    Ellipse,
    Text
}

public sealed class NetworkMap
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public Guid? LocationId { get; set; }

    public Guid? BuildingId { get; set; }

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public List<NetworkMapNode> Nodes { get; set; } = [];

    public List<NetworkMapConnection> Connections { get; set; } = [];

    public List<NetworkMapAnnotation> Annotations { get; set; } = [];
}

public sealed class NetworkMapNode
{
    public Guid Id { get; set; }

    public NetworkMapNodeKind Kind { get; set; } = NetworkMapNodeKind.Device;

    public Guid DeviceId { get; set; }

    public Guid? GroupId { get; set; }

    public Guid? LocationId { get; set; }

    public MapIconKind Icon { get; set; } = MapIconKind.Router;

    public double X { get; set; } = 120;

    public double Y { get; set; } = 100;

    public double Width { get; set; } = 120;

    public double Height { get; set; } = 76;
}

public sealed class NetworkMapConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid FromDeviceId { get; set; }

    public Guid ToDeviceId { get; set; }

    public Guid? FromNodeId { get; set; }

    public Guid? ToNodeId { get; set; }

    public MapConnectionKind Kind { get; set; } = MapConnectionKind.Ethernet;

    public double StrokeWidth { get; set; } = 2.5;

    public string? Label { get; set; }
}

public sealed class NetworkMapAnnotation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public NetworkMapAnnotationKind Kind { get; set; }

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public string StrokeColor { get; set; } = "#64748b";

    public string? FillColor { get; set; }

    public double StrokeWidth { get; set; } = 2;

    public string? Text { get; set; }
}

public sealed class NetworkMapDocument
{
    public List<NetworkMap> Maps { get; set; } = [];
}
