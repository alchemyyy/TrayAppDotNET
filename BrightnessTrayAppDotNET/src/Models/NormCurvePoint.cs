namespace BrightnessTrayAppDotNET.Models;

/// <summary>
/// One control point on a per-monitor brightness norm curve.
/// X is on the 0..100 input axis; Y is the signed offset on the editor's zoomable axis.
/// Persisted on <see cref="MonitorOverrideEntry"/> as an &lt;XmlArray&gt; so each row carries its own curve.
/// </summary>
public class NormCurvePoint
{
    public double X { get; set; }
    public double Y { get; set; }
}
