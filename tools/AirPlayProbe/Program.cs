using System.Xml.Linq;

var url = "http://192.168.1.29:60006/upnp/desc/aios_device/aios_device.xml";
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
var xml = await http.GetStringAsync(url);

var doc = XDocument.Parse(xml);
var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

// Gefixte Logik: erstes Device finden, dessen deviceType "MediaRenderer" enthält.
XElement? device = null;
foreach (var d in doc.Descendants(ns + "device"))
{
    var dt = d.Element(ns + "deviceType")?.Value ?? string.Empty;
    if (dt.Contains("MediaRenderer", StringComparison.OrdinalIgnoreCase))
    {
        device = d;
        break;
    }
}

if (device is null)
{
    Console.WriteLine("✗ Kein MediaRenderer gefunden");
    return;
}

Console.WriteLine("✓ MediaRenderer-Device gefunden:");
Console.WriteLine($"  deviceType:  {device.Element(ns + "deviceType")?.Value}");
Console.WriteLine($"  friendlyName: '{device.Element(ns + "friendlyName")?.Value.Trim()}'");
Console.WriteLine($"  UDN:         {device.Element(ns + "UDN")?.Value}");
Console.WriteLine($"  modelName:   {device.Element(ns + "modelName")?.Value}");
Console.WriteLine();

Console.WriteLine("Services im MediaRenderer:");
foreach (var svc in device.Descendants(ns + "service"))
{
    var stype = svc.Element(ns + "serviceType")?.Value ?? "?";
    var ctrl = svc.Element(ns + "controlURL")?.Value ?? "?";
    Console.WriteLine($"  {stype}  →  controlURL={ctrl}");
}
