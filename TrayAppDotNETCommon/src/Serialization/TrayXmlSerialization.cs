using System.Xml;

namespace TrayAppDotNETCommon.Serialization;

public interface ITrayXmlTypeSerializer
{
    Type TargetType { get; }
    object Read(Stream stream);
    void Write(Stream stream, object value);
}

public interface ITrayXmlSerializationCallbacks
{
    void OnTrayXmlSerializing() { }
    void OnTrayXmlDeserializing() { }
    void OnTrayXmlDeserialized() { }
}

/// <summary>
/// Receives enum values the generated reader could not map to a defined member. The property keeps its initializer
/// value; the type decides whether the whole object is still usable (a hotkey binding with an unknown action is not).
/// </summary>
public interface ITrayXmlUnknownValueHandler
{
    void OnTrayXmlUnknownValue(string memberName, string value);
}

public static class TrayXmlSerializer
{
    private static readonly Dictionary<Type, ITrayXmlTypeSerializer> Serializers = [];
    private static readonly Lock Gate = new();

    public static XmlWriterSettings WriterSettings { get; } = new()
    {
        Indent = true,
        IndentChars = "  ",
        NewLineChars = Environment.NewLine,
        NewLineHandling = NewLineHandling.Replace
    };

    public static XmlReaderSettings ReaderSettings { get; } = new()
    {
        IgnoreComments = true, IgnoreProcessingInstructions = true, IgnoreWhitespace = true
    };

    public static void Register(ITrayXmlTypeSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        lock (Gate) Serializers[serializer.TargetType] = serializer;
    }

    public static T Read<T>(Stream stream)
    {
        ITrayXmlTypeSerializer serializer = GetSerializer(typeof(T));
        return (T)serializer.Read(stream);
    }

    public static T ReadFile<T>(string path)
    {
        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete);
        return Read<T>(stream);
    }

    /// <summary>
    /// Reads <paramref name="path"/> and reports whether it was missing, loaded, corrupt or locked (see
    /// <see cref="DataFileLoader.TryLoad{T}"/>). Changes nothing on disk. <paramref name="value"/> is set only for
    /// <see cref="DataFileLoadResult.Loaded"/>. A type without a generated serializer is a programming error and
    /// throws instead of being reported as a bad file.
    /// </summary>
    public static DataFileLoadResult TryLoadFile<T>(
        string path,
        out T? value,
        Action<string> log)
        where T : notnull
    {
        ITrayXmlTypeSerializer serializer = GetSerializer(typeof(T));
        DataFileLoadResult result = DataFileLoader.TryLoad(path, stream => (T)serializer.Read(stream), log, out value);
        return result;
    }

    public static void Write<T>(Stream stream, T value)
        where T : notnull =>
        Write(stream, (object)value);

    public static void Write(Stream stream, object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ITrayXmlTypeSerializer serializer = GetSerializer(value.GetType());
        serializer.Write(stream, value);
    }

    /// <summary>Serializes <paramref name="value"/> and replaces <paramref name="path"/> atomically.</summary>
    public static void WriteFile<T>(string path, T value)
        where T : notnull
    {
        ITrayXmlTypeSerializer serializer = GetSerializer(value.GetType());
        AtomicFile.Write(path, stream => serializer.Write(stream, value));
    }

    private static ITrayXmlTypeSerializer GetSerializer(Type type)
    {
        lock (Gate)
        {
            if (Serializers.TryGetValue(type, out ITrayXmlTypeSerializer? serializer))
                return serializer;
        }

        throw new InvalidOperationException($"No generated XML serializer is registered for '{type.FullName}'.");
    }
}
