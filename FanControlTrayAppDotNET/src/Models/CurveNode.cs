using System.Xml.Serialization;

namespace FanControlTrayAppDotNET.Models;

// One control point on a Curve. X is the input axis (temperature, load, etc. depending on the
// curve's SelectedDataSource); Y is the output axis (duty cycle % or RPM depending on the fan
// the curve is assigned to). Both are doubles since interpolation needs sub-integer precision.
public class CurveNode
{
    [XmlAttribute] public double X { get; set; }

    [XmlAttribute] public double Y { get; set; }

    public CurveNode() { }

    public CurveNode(double x, double y)
    {
        X = x;
        Y = y;
    }
}
