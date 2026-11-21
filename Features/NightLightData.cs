using System.IO;

namespace YsfUtil.Features;

/// <summary>
/// Reads and writes the registry blobs in which Windows keeps night light (CloudStore format):
/// <c>43 42 01 00 0A 02 01 00 2A 06 &lt;epoch&gt; 2A 2B 0E &lt;length&gt; 43 42 01 &lt;body&gt;</c>,
/// numbers as little-endian base-128. Windows ignores a write whose epoch is not newer than the last.
/// Anything not in this shape is refused rather than guessed at.
/// </summary>
internal sealed class NightLightData
{
    private static readonly byte[] Header = [0x43, 0x42, 0x01, 0x00, 0x0A, 0x02, 0x01, 0x00, 0x2A, 0x06];
    private static readonly byte[] BodyTag = [0x2A, 0x2B, 0x0E];
    private static readonly byte[] Magic = [0x43, 0x42, 0x01];
    private static readonly byte[] Active = [0x10, 0x00];
    private static readonly byte[] ModifiedTag = [0xC6, 0x14];
    private static readonly byte[] TemperatureTag = [0xCF, 0x28];
    private static readonly byte[] SunsetTag = [0xCA, 0x32];

    /// <summary>Windows' night light range: strength 100% is 1200 K, 0% is 6500 K (no tint).</summary>
    public const int WarmestKelvin = 1200;
    public const int NeutralKelvin = 6500;
    /// <summary>What Windows uses when no strength was ever set.</summary>
    public const int DefaultKelvin = 4000;

    private NightLightData(ulong epoch, List<byte> body)
    {
        Epoch = epoch;
        Body = body;
    }

    public ulong Epoch { get; }

    /// <summary>Everything after the second magic, including its trailing zeros.</summary>
    public List<byte> Body { get; }

    public static NightLightData Parse(byte[] data)
    {
        int at = 0;
        Expect(data, ref at, Header);
        ulong epoch = ReadNumber(data, ref at);
        Expect(data, ref at, BodyTag);
        ulong length = ReadNumber(data, ref at);
        Expect(data, ref at, Magic);
        if ((ulong)(data.Length - at) != length || length < 5 || data[at] != 0)
            throw new InvalidDataException("Windows stores night light in an unknown format.");
        return new NightLightData(epoch, data.Skip(at).ToList());
    }

    /// <summary>The blob to write: same content, epoch newer than the stored one.</summary>
    public byte[] ToBytes(DateTimeOffset now)
    {
        ulong epoch = Math.Max((ulong)now.ToUnixTimeSeconds(), Epoch + 2);
        var bytes = new List<byte>(Header);
        WriteNumber(bytes, epoch);
        bytes.AddRange(BodyTag);
        WriteNumber(bytes, (ulong)Body.Count);
        bytes.AddRange(Magic);
        bytes.AddRange(Body);
        return [.. bytes];
    }

    // ── State blob ──────────────────────────────────────────────────────────

    /// <summary>State blob: <c>10 00</c> right after the leading zero means night light is on.</summary>
    public bool IsOn => Body.Count > 2 && Body[1] == Active[0] && Body[2] == Active[1];

    public NightLightData WithOn(bool on, DateTimeOffset now)
    {
        var body = new List<byte>(Body);
        if (IsOn && !on) body.RemoveRange(1, Active.Length);
        else if (!IsOn && on) body.InsertRange(1, Active);

        // The state also carries when it changed (FILETIME after C6 14).
        int at = Find(body, ModifiedTag);
        if (at < 0) throw new InvalidDataException("Windows stores night light in an unknown format.");
        int start = at + ModifiedTag.Length, end = start;
        ReadNumber(body, ref end);
        var time = new List<byte>();
        WriteNumber(time, (ulong)now.UtcDateTime.ToFileTimeUtc());
        body.RemoveRange(start, end - start);
        body.InsertRange(start, time);
        return new NightLightData(Epoch, body);
    }

    // ── Settings blob ───────────────────────────────────────────────────────

    /// <summary>Settings blob: colour temperature after <c>CF 28</c>, stored doubled (zigzag).</summary>
    public int Kelvin
    {
        get
        {
            int at = Find(Body, TemperatureTag);
            if (at < 0) return DefaultKelvin;
            at += TemperatureTag.Length;
            return (int)(ReadNumber(Body, ref at) >> 1);
        }
    }

    public NightLightData WithKelvin(int kelvin)
    {
        kelvin = Math.Clamp(kelvin, WarmestKelvin, NeutralKelvin);
        var value = new List<byte>(TemperatureTag);
        WriteNumber(value, (ulong)kelvin << 1);
        var body = new List<byte>(Body);
        int at = Find(body, TemperatureTag);
        if (at >= 0)
        {
            int end = at + TemperatureTag.Length;
            ReadNumber(body, ref end);
            body.RemoveRange(at, end - at);
        }
        else
        {
            // Windows writes the strength right before the sunset time.
            at = Find(body, SunsetTag);
            if (at < 0) throw new InvalidDataException("Windows stores night light in an unknown format.");
        }
        body.InsertRange(at, value);
        return new NightLightData(Epoch, body);
    }

    /// <summary>Strength as Windows Settings shows it, 0–100.</summary>
    public static int Strength(int kelvin) =>
        (int)Math.Round((NeutralKelvin - Math.Clamp(kelvin, WarmestKelvin, NeutralKelvin)) * 100.0 / (NeutralKelvin - WarmestKelvin));

    // ── Encoding ────────────────────────────────────────────────────────────

    private static void Expect(IReadOnlyList<byte> data, ref int at, byte[] expected)
    {
        foreach (byte b in expected)
        {
            if (at >= data.Count || data[at] != b)
                throw new InvalidDataException("Windows stores night light in an unknown format.");
            at++;
        }
    }

    private static ulong ReadNumber(IReadOnlyList<byte> data, ref int at)
    {
        ulong value = 0;
        for (int shift = 0; shift < 64; shift += 7)
        {
            if (at >= data.Count) break;
            byte b = data[at++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return value;
        }
        throw new InvalidDataException("Windows stores night light in an unknown format.");
    }

    private static void WriteNumber(List<byte> bytes, ulong value)
    {
        do
        {
            byte b = (byte)(value & 0x7F);
            value >>= 7;
            bytes.Add(value != 0 ? (byte)(b | 0x80) : b);
        } while (value != 0);
    }

    private static int Find(List<byte> data, byte[] tag)
    {
        for (int i = 1; i + tag.Length <= data.Count; i++)
        {
            if (data[i] == tag[0] && data[i + 1] == tag[1]) return i;
        }
        return -1;
    }
}
