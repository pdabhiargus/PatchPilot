using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PatchPilot;

/// <summary>Bounded, strict JSON serialization and cryptographic artifact utilities.</summary>
public static class JsonFiles
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    public static T Parse<T>(string value) =>
        JsonSerializer.Deserialize<T>(value, Options) ?? throw new InvalidDataException("Empty JSON.");
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Read<T>(string path)
    {
        if (new FileInfo(path).Length > 4_000_000) throw new InvalidDataException("Artifact exceeds limit.");
        return Parse<T>(File.ReadAllText(path));
    }
    public static void Write<T>(string path, T value)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, Serialize(value), new UTF8Encoding(false));
        File.Move(temp, full, true);
    }
    public static string Hash(string text) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    public static string Secret(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value : throw new InvalidOperationException("Missing variable: " + name);
    public static Envelope Sign(Bundle bundle)
    {
        var payload = Serialize(bundle);
        return new(payload, Convert.ToHexString(HMACSHA256.HashData(Key(), Encoding.UTF8.GetBytes(payload))));
    }
    public static Bundle Authenticate(Envelope envelope)
    {
        var actual = HMACSHA256.HashData(Key(), Encoding.UTF8.GetBytes(envelope.Payload));
        byte[] supplied;
        try { supplied = Convert.FromHexString(envelope.Signature); }
        catch (FormatException) { throw new InvalidDataException("Invalid artifact signature."); }
        if (!CryptographicOperations.FixedTimeEquals(actual, supplied))
            throw new InvalidDataException("Invalid artifact signature.");
        var bundle = Parse<Bundle>(envelope.Payload);
        if (bundle.Schema != 1 || bundle.ValidatedAt > DateTimeOffset.UtcNow.AddMinutes(1) ||
            bundle.ValidatedAt < DateTimeOffset.UtcNow.AddHours(-24))
            throw new InvalidDataException("Expired or unsupported bundle.");
        return bundle;
    }
    private static byte[] Key()
    {
        var key = Convert.FromBase64String(Secret("PP_ATTESTATION_KEY"));
        if (key.Length < 32) throw new InvalidDataException("Attestation key requires at least 32 random bytes.");
        return key;
    }
}
