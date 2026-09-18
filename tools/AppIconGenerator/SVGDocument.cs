using System.Globalization;
using System.Reflection;
using System.Xml.Linq;
using SkiaSharp;

namespace TrayAppDotNET.Tools.AppIconGenerator;

/// <summary>Effective fill and stroke paint state for one path element, after style inheritance.</summary>
internal readonly record struct PathPaintStyle(
    bool HasFill,
    bool HasStroke,
    float StrokeWidth,
    SKStrokeCap StrokeCap,
    SKStrokeJoin StrokeJoin,
    float StrokeMiterLimit)
{
    /// <summary>The SVG initial values: filled black, no stroke, per the CSS/SVG property defaults.</summary>
    public static readonly PathPaintStyle Default = new(
        HasFill: true,
        HasStroke: false,
        StrokeWidth: 1f,
        StrokeCap: SKStrokeCap.Butt,
        StrokeJoin: SKStrokeJoin.Miter,
        StrokeMiterLimit: 4f);
}

/// <summary>Loads the path geometry and view box from one embedded SVG source.</summary>
internal sealed class SVGDocument : IDisposable
{
    private const string ResourcePrefix = "AppIconGenerator.SVG.";
    private const string NoneKeyword = "none";
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
        return Load(stream, resourceFileName);
    }

    /// <summary>Loads SVG path geometry and view box from an arbitrary stream. Shared by embedded sources and tests.</summary>
    internal static SVGDocument Load(Stream stream, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        XDocument document = XDocument.Load(stream, LoadOptions.SetLineInfo);
        XElement root = document.Root
                        ?? throw new InvalidDataException($"SVG '{sourceName}' has no root element.");
        ValidateSupportedStructure(root, sourceName);

        SKRect viewBox = ParseViewBox(root, sourceName);
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

                using SKPath localPath = SKPath.ParseSvgPathData(pathData)
                                          ?? throw new InvalidDataException(
                                              $"SVG '{sourceName}' contains invalid path data.");
                PathPaintStyle paintStyle = ComputeEffectivePaintStyle(pathElement, sourceName);
                using SKPath localGeometry = BuildLocalFillGeometry(localPath, paintStyle);
                if (localGeometry.IsEmpty) continue;

                SKMatrix elementTransform = ComputeCumulativeTransform(pathElement, sourceName);
                combinedPath.AddPath(localGeometry, in elementTransform);
                pathCount++;
            }

            if (pathCount == 0 || combinedPath.IsEmpty)
                throw new InvalidDataException($"SVG '{sourceName}' contains no drawable paths.");

            return new SVGDocument(combinedPath, viewBox);
        }
        catch
        {
            combinedPath.Dispose();
            throw;
        }
    }

    /// <summary>Maps this document's view box into a normalized composition rectangle, preserving aspect ratio.</summary>
    public SKPath CreateNormalizedPath(NormalizedRectangle destination)
    {
        ValidateDestination(destination);

        // Both axes share one scale derived from the view box's larger dimension, so a non-square
        // view box is contained (not stretched) and centered on its shorter axis. When the view box
        // is square this reduces exactly to the old per-axis scale, so square sources are unaffected.
        float viewBoxMaxDimension = Math.Max(_viewBox.Width, _viewBox.Height);
        float scaleX = destination.Width / viewBoxMaxDimension;
        float scaleY = destination.Height / viewBoxMaxDimension;
        float centeringOffsetX = destination.Width * (viewBoxMaxDimension - _viewBox.Width) / (2f * viewBoxMaxDimension);
        float centeringOffsetY = destination.Height * (viewBoxMaxDimension - _viewBox.Height) / (2f * viewBoxMaxDimension);
        float translateX = destination.X + centeringOffsetX - _viewBox.Left * scaleX;
        float translateY = destination.Y + centeringOffsetY - _viewBox.Top * scaleY;
        SKMatrix transform = SKMatrix.CreateScaleTranslation(scaleX, scaleY, translateX, translateY);
        SKPath transformedPath = new();
        _path.Transform(transform, transformedPath);
        return transformedPath;
    }

    private static void ValidateSupportedStructure(XElement root, string resourceFileName)
    {
        bool hasUnsupportedElements = root.Descendants().Any(static element =>
            element.Name.LocalName is "clipPath" or "mask" or "use");
        if (hasUnsupportedElements)
            throw new NotSupportedException(
                $"SVG '{resourceFileName}' contains clipping, masking, or referenced geometry.");
    }

    /// <summary>Builds one path element's local-space geometry: its fill, its stroke outline, or both.</summary>
    private static SKPath BuildLocalFillGeometry(SKPath localPath, PathPaintStyle paintStyle)
    {
        SKPath geometry = new() { FillType = SKPathFillType.Winding };
        if (paintStyle.HasFill)
            geometry.AddPath(localPath);

        if (paintStyle.HasStroke && paintStyle.StrokeWidth > 0f)
        {
            using SKPaint strokePaint = new()
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = paintStyle.StrokeWidth,
                StrokeCap = paintStyle.StrokeCap,
                StrokeJoin = paintStyle.StrokeJoin,
                StrokeMiter = paintStyle.StrokeMiterLimit
            };
            using SKPath strokeOutline = new();
            strokePaint.GetFillPath(localPath, strokeOutline);
            geometry.AddPath(strokeOutline);
        }

        return geometry;
    }

    /// <summary>Resolves the effective fill and stroke style for a path element by walking its ancestors.</summary>
    private static PathPaintStyle ComputeEffectivePaintStyle(XElement pathElement, string resourceFileName)
    {
        PathPaintStyle style = PathPaintStyle.Default;
        foreach (XElement ancestorElement in pathElement.AncestorsAndSelf().Reverse())
            style = ApplyStyleOverrides(style, ancestorElement, resourceFileName);

        return style;
    }

    /// <summary>Applies one element's "style" attribute and presentation attributes on top of an inherited style.</summary>
    private static PathPaintStyle ApplyStyleOverrides(PathPaintStyle style, XElement element, string resourceFileName)
    {
        Dictionary<string, string> declarations = ParseStyleDeclarations(element.Attribute("style")?.Value);

        string? fillValue = ReadStyleProperty(element, declarations, "fill");
        string? strokeValue = ReadStyleProperty(element, declarations, "stroke");
        string? strokeWidthValue = ReadStyleProperty(element, declarations, "stroke-width");
        string? strokeLinecapValue = ReadStyleProperty(element, declarations, "stroke-linecap");
        string? strokeLinejoinValue = ReadStyleProperty(element, declarations, "stroke-linejoin");
        string? strokeMiterLimitValue = ReadStyleProperty(element, declarations, "stroke-miterlimit");

        bool hasFill = fillValue == null ? style.HasFill : !IsNoneKeyword(fillValue);
        bool hasStroke = strokeValue == null ? style.HasStroke : !IsNoneKeyword(strokeValue);
        float strokeWidth = strokeWidthValue == null
            ? style.StrokeWidth
            : ParseFloatValue(strokeWidthValue, resourceFileName);
        SKStrokeCap strokeCap = strokeLinecapValue == null
            ? style.StrokeCap
            : ParseStrokeCap(strokeLinecapValue, resourceFileName);
        SKStrokeJoin strokeJoin = strokeLinejoinValue == null
            ? style.StrokeJoin
            : ParseStrokeJoin(strokeLinejoinValue, resourceFileName);
        float strokeMiterLimit = strokeMiterLimitValue == null
            ? style.StrokeMiterLimit
            : ParseFloatValue(strokeMiterLimitValue, resourceFileName);

        return new PathPaintStyle(hasFill, hasStroke, strokeWidth, strokeCap, strokeJoin, strokeMiterLimit);
    }

    private static Dictionary<string, string> ParseStyleDeclarations(string? styleText)
    {
        Dictionary<string, string> declarations = new();
        if (string.IsNullOrWhiteSpace(styleText)) return declarations;

        string[] declarationTexts = styleText.Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (string declarationText in declarationTexts)
        {
            int colonIndex = declarationText.IndexOf(':');
            if (colonIndex < 0) continue;

            string propertyName = declarationText[..colonIndex].Trim();
            string propertyValue = declarationText[(colonIndex + 1)..].Trim();
            if (propertyName.Length > 0 && propertyValue.Length > 0)
                declarations[propertyName] = propertyValue;
        }

        return declarations;
    }

    /// <summary>Reads one style property, preferring the inline "style" attribute over the presentation attribute.</summary>
    private static string? ReadStyleProperty(XElement element, Dictionary<string, string> declarations, string propertyName) =>
        declarations.TryGetValue(propertyName, out string? declaredValue)
            ? declaredValue
            : element.Attribute(propertyName)?.Value;

    private static bool IsNoneKeyword(string value) =>
        string.Equals(value.Trim(), NoneKeyword, StringComparison.OrdinalIgnoreCase);

    private static float ParseFloatValue(string value, string resourceFileName)
    {
        if (!float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float result)
            || !float.IsFinite(result))
            throw new InvalidDataException($"SVG '{resourceFileName}' has an invalid numeric style value '{value}'.");

        return result;
    }

    private static SKStrokeCap ParseStrokeCap(string value, string resourceFileName) =>
        value.Trim().ToLowerInvariant() switch
        {
            "butt" => SKStrokeCap.Butt,
            "round" => SKStrokeCap.Round,
            "square" => SKStrokeCap.Square,
            _ => throw new NotSupportedException(
                $"SVG '{resourceFileName}' has an unsupported stroke-linecap value '{value}'.")
        };

    private static SKStrokeJoin ParseStrokeJoin(string value, string resourceFileName) =>
        value.Trim().ToLowerInvariant() switch
        {
            "miter" => SKStrokeJoin.Miter,
            "round" => SKStrokeJoin.Round,
            "bevel" => SKStrokeJoin.Bevel,
            _ => throw new NotSupportedException(
                $"SVG '{resourceFileName}' has an unsupported stroke-linejoin value '{value}'.")
        };

    /// <summary>Combines the "transform" attributes of a path element and its ancestors into one matrix.</summary>
    private static SKMatrix ComputeCumulativeTransform(XElement pathElement, string resourceFileName)
    {
        SKMatrix combined = SKMatrix.Identity;
        foreach (XElement ancestorElement in pathElement.AncestorsAndSelf().Reverse())
        {
            string? transformText = ancestorElement.Attribute("transform")?.Value;
            if (string.IsNullOrWhiteSpace(transformText)) continue;

            SKMatrix elementMatrix = ParseTransformList(transformText, resourceFileName);
            combined = combined.PreConcat(elementMatrix);
        }

        return combined;
    }

    /// <summary>Parses a whitespace- or comma-separated SVG "transform" attribute into one matrix.</summary>
    private static SKMatrix ParseTransformList(string transformText, string resourceFileName)
    {
        SKMatrix combined = SKMatrix.Identity;
        int position = 0;
        while (position < transformText.Length)
        {
            while (position < transformText.Length && IsTransformListSeparator(transformText[position])) position++;
            if (position >= transformText.Length) break;

            int nameStart = position;
            while (position < transformText.Length && transformText[position] != '(') position++;
            if (position >= transformText.Length)
                throw new InvalidDataException($"SVG '{resourceFileName}' has a malformed transform attribute.");
            string functionName = transformText[nameStart..position].Trim();

            int argumentsStart = position + 1;
            int closingParenthesis = transformText.IndexOf(')', argumentsStart);
            if (closingParenthesis < 0)
                throw new InvalidDataException($"SVG '{resourceFileName}' has a malformed transform attribute.");

            float[] arguments = ParseTransformArguments(
                transformText[argumentsStart..closingParenthesis],
                resourceFileName);
            SKMatrix functionMatrix = CreateTransformFunctionMatrix(functionName, arguments, resourceFileName);
            combined = combined.PreConcat(functionMatrix);

            position = closingParenthesis + 1;
        }

        return combined;
    }

    private static bool IsTransformListSeparator(char character) =>
        character is ' ' or '\t' or '\r' or '\n' or ',';

    private static float[] ParseTransformArguments(string argumentsText, string resourceFileName)
    {
        string[] tokens = argumentsText.Split(ViewBoxSeparators, StringSplitOptions.RemoveEmptyEntries);
        float[] arguments = new float[tokens.Length];
        for (int tokenIndex = 0; tokenIndex < tokens.Length; tokenIndex++)
        {
            if (!float.TryParse(tokens[tokenIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                || !float.IsFinite(value))
                throw new InvalidDataException($"SVG '{resourceFileName}' has an invalid transform argument.");
            arguments[tokenIndex] = value;
        }

        return arguments;
    }

    /// <summary>Builds the matrix for one SVG transform function; extend this switch before adding sources that need more functions.</summary>
    private static SKMatrix CreateTransformFunctionMatrix(
        string functionName,
        float[] arguments,
        string resourceFileName) =>
        (functionName, arguments.Length) switch
        {
            ("matrix", 6) => new SKMatrix(
                arguments[0], arguments[2], arguments[4],
                arguments[1], arguments[3], arguments[5],
                0f, 0f, 1f),
            ("rotate", 1) => SKMatrix.CreateRotationDegrees(arguments[0]),
            ("rotate", 3) => SKMatrix.CreateRotationDegrees(arguments[0], arguments[1], arguments[2]),
            _ => throw new NotSupportedException(
                $"SVG '{resourceFileName}' contains transform function '{functionName}' " +
                $"with {arguments.Length} argument(s), which is not supported.")
        };

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
