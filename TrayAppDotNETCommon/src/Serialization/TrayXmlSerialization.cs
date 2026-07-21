using System.Diagnostics.CodeAnalysis;
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

public static class TrayXmlSerializer
{
    private static readonly Dictionary<Type, ITrayXmlTypeSerializer> Serializers = [];
    private static readonly Lock Gate = new();
    private static readonly Lock FileWriteGate = new();

    public static XmlWriterSettings WriterSettings { get; } = new()
    {
        Indent = true,
        IndentChars = "  ",
        NewLineChars = Environment.NewLine,
        NewLineHandling = NewLineHandling.Replace
    };

    public static XmlReaderSettings ReaderSettings { get; } = new()
    {
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = true
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

    public static bool TryReadFile<T>(
        string path,
        [NotNullWhen(true)] out T? value,
        Action<Exception>? logError = null)
        where T : notnull
    {
        try
        {
            if (File.Exists(path))
            {
                value = ReadFile<T>(path);
                return true;
            }
        }
        catch (Exception ex)
        {
            logError?.Invoke(ex);
        }

        value = default;
        return false;
    }

    public static T LoadFileOrDefault<T>(
        string path,
        Func<T> createDefault,
        Action<Exception>? logError = null)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(createDefault);
        return TryReadFile(path, out T? value, logError) ? value : createDefault();
    }

    public static T LoadFileOrDefault<T>(string path, Action<Exception>? logError = null)
        where T : notnull, new() =>
        LoadFileOrDefault(path, static () => new T(), logError);

    public static void Write<T>(Stream stream, T value)
        where T : notnull =>
        Write(stream, (object)value);

    public static void Write(Stream stream, object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ITrayXmlTypeSerializer serializer = GetSerializer(value.GetType());
        serializer.Write(stream, value);
    }

    public static void WriteFile<T>(string path, T value)
        where T : notnull
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        lock (FileWriteGate)
        {
            string temporaryPath = $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
            try
            {
                using (FileStream stream = new(
                           temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None))
                {
                    Write(stream, value);
                    stream.Flush();
                }

                ReplaceFileWithRetry(temporaryPath, path);
            }
            catch
            {
                try
                {
                    if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                }
                catch (Exception cleanupException)
                {
                    TADNLog.Log(
                        $"TrayXmlSerializer.WriteFile: temporary file cleanup failed: {cleanupException.Message}");
                }

                throw;
            }
        }
    }

    public static bool TryWriteFile<T>(string path, T value, Action<Exception>? logError = null)
        where T : notnull
    {
        try
        {
            WriteFile(path, value);
            return true;
        }
        catch (Exception ex)
        {
            logError?.Invoke(ex);
            return false;
        }
    }

    private static void ReplaceFileWithRetry(string temporaryPath, string path)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temporaryPath, path, overwrite: true);
                return;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException &&
                attempt < TimeConstants.SettingsFileReplaceRetryCount)
            {
                Thread.Sleep(TimeConstants.SettingsFileReplaceRetryDelayMs);
            }
        }
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
