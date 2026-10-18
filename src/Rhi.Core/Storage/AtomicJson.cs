using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rhi.Core.Storage;

public static class AtomicJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path,
        System.Text.Encoding.UTF8), Options) ?? throw new InvalidDataException(path);

    public static void Write<T>(string path, T value)
    {
        Deployment.PathPolicy.CheckNoLinks(path);
        var pending = path + "." + Guid.NewGuid().ToString("N") + ".pending";
        using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            4096, FileOptions.WriteThrough))
        {
            JsonSerializer.Serialize(stream, value, Options);
            stream.Flush(flushToDisk: true);
        }
        File.Move(pending, path, overwrite: true);
    }

    public static string Fingerprint<T>(T value) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, Options)));
}
