using System.Globalization;
using System.Reflection;
using System.Xml.Linq;
using SkiaSharp;

namespace TrayAppDotNET.Tools.AppIconGenerator;

/// <summary>Loads the path geometry and view box from one embedded SVG source.</summary>
internal sealed class SVGDocument : IDisposable
{
    private const string ResourcePrefix = "AppIconGenerator.SVG.";
    private static readonly char[] ViewBoxSeparators = [' ', ',', '\t', '\r', '\n'];

    private readonly SKPath _path;
    private readonly SKRect _viewBox;

    private SVGDocument(SKPath path, SKRect viewBox)
    {
        _path = path;
        _viewBox = viewBox;
    }

    /// <summary>Loads an SVG embedded in the generator assembly.</summary>
    public static SVGDocument LoadEmbedded(string resourceFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceFileName);

        Assembly assembly = typeof(SVGDocument).Assembly;
        string resourceName = ResourcePrefix + resourceFileName;
        using Stream stream = assembly.GetManifestResourceStream(resourceName)
                              ?? throw new InvalidOperationException(
                                  $"Embedded SVG resource '{resourceName}' was not found.");
        XDocument document = XDocument.Load(stream, LoadOptions.SetLineInfo);
        XElement root = document.Root
                        ?? throw new InvalidDataException($"SVG '{resourceFileName}' has no root element.");
        ValidateSupportedStructure(root, resourceFileName);

        SKRect viewBox = ParseViewBox(root, resourceFileName);
        SKPath combinedPath = new();
        try
        {
            IEnumerable<XElement> pathElements = root.Descendants()
                .Where(static element => element.Name.LocalName == "path");
            int pathCount = 0;
            foreach (XElement pathElement in pathElements)
            {
                string? pathData = pathElement.Attribute("d")?.Value;
                if (string.IsNullOrWhiteSpace(pathData)) continue;

                using SKPath parsedPath = SKPath.ParseSvgPathData(pathData)
                                          ?? throw new InvalidDataException(
                                              $"SVG '{resourceFileName}' contains invalid path data.");
                combinedPath.AddPath(parsedPath);
                pathCount++;
            }

            if (pathCount == 0 || combinedPath.IsEmpty)
                throw new InvalidDataException($"SVG '{resourceFileName}' contains no drawable paths.");

            return new SVGDocument(combinedPath, viewBox);
        }
        catch
        {
            combinedPath.Dispose();
            throw;
        }
    }

    /// <summary>Maps this document's view box into a normalized composition rectangle.</summary>
    public SKPath CreateNormalizedPath(NormalizedRectangle destination)
    {
        ValidateDestination(destination);

        float scaleX = destination.Width / _viewBox.Width;
        float scaleY = destination.Height / _viewBox.Height;
        float translateX = destination.X - _viewBox.Left * scaleX;
        float translateY = destination.Y - _viewBox.Top * scaleY;
        SKMatrix transform = SKMatrix.CreateScaleTranslation(scaleX, scaleY, translateX, translateY);
        SKPath transformedPath = new();
        _path.Transform(transform, transformedPath);
        return transformedPath;
    }

    private static void ValidateSupportedStructure(XElement root, string resourceFileName)
    {
        IEnumerable<XAttribute> transforms = root.DescendantsAndSelf().Attributes("transform");
        if (transforms.Any())
            throw new NotSupportedException(
                $"SVG '{resourceFileName}' contains transforms; convert them to path coordinates first.");

        bool hasUnsupportedElements = root.Descendants().Any(static element =>
            element.Name.LocalName is "clipPath" or "mask" or "use");
        if (hasUnsupportedElements)
            throw new NotSupportedException(
                $"SVG '{resourceFileName}' contains clipping, masking, or referenced geometry.");
    }

    private static SKRect ParseViewBox(XElement root, string resourceFileName)
    {
        string? viewBoxText = root.Attribute("viewBox")?.Value;
        if (string.IsNullOrWhiteSpace(viewBoxText))
            throw new InvalidDataException($"SVG '{resourceFileName}' has no viewBox.");

        string[] values = viewBoxText.Split(ViewBoxSeparators, StringSplitOptions.RemoveEmptyEntries);
        if (values.Length != 4
            || !float.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float left)
            || !float.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float top)
            || !float.TryParse(values[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float width)
            || !float.TryParse(values[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float height)
            || !float.IsFinite(left)
            || !float.IsFinite(top)
            || !float.IsFinite(width)
            || !float.IsFinite(height)
            || width <= 0
            || height <= 0)
            throw new InvalidDataException($"SVG '{resourceFileName}' has an invalid viewBox.");

        return new SKRect(left, top, left + width, top + height);
    }

    private static void ValidateDestination(NormalizedRectangle destination)
    {
        if (!float.IsFinite(destination.X)
            || !float.IsFinite(destination.Y)
            || !float.IsFinite(destination.Width)
            || !float.IsFinite(destination.Height)
            || destination.Width <= 0
            || destination.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(destination));
    }

    public void Dispose() => _path.Dispose();
}
