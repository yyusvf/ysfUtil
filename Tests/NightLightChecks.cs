using YsfUtil.Features;

internal static class NightLightChecks
{
    private static byte[] Hex(string text) => text.Split(' ').Select(b => Convert.ToByte(b, 16)).ToArray();

    public static void Run()
    {
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        static bool Fails(Action action) { try { action(); return false; } catch (InvalidDataException) { return true; } }

        // Read from Windows 11: night light off (switched by hand), strength 3850 K = 50%.
        byte[] off = Hex("43 42 01 00 0A 02 01 00 2A 06 B6 BA 86 D8 06 2A 2B 0E 13 43 42 01 00 D0 0A 02 C6 14 D6 84 FF F7 C9 B7 DE EE 01 00 00 00 00");
        byte[] settings = Hex("43 42 01 00 0A 02 01 00 2A 06 E1 93 87 D8 06 2A 2B 0E 26 43 42 01 00 02 01 C2 0A 00 CA 14 0E 17 00 CA 1E 0E 07 00 CF 28 94 3C CA 32 0E 12 2E 2C 00 CA 3C 0E 06 2E 13 00 00 00 00 00");
        var now = new DateTimeOffset(2026, 11, 21, 17, 0, 0, TimeSpan.Zero);

        NightLightData state = NightLightData.Parse(off);
        Check(!state.IsOn, "Night light read as on.");
        Check(state.ToBytes(DateTimeOffset.FromUnixTimeSeconds(0)).Skip(15).SequenceEqual(off.Skip(15)), "Rewriting changed the content.");
        byte[] onBytes = state.WithOn(true, now).ToBytes(now);
        NightLightData on = NightLightData.Parse(onBytes);
        Check(on.IsOn && onBytes.Length == off.Length + 2, "Switching on did not add the on marker.");
        Check(on.Epoch > state.Epoch, "A write was not newer than the stored one.");
        Check(NightLightData.Parse(on.ToBytes(DateTimeOffset.FromUnixTimeSeconds(0))).Epoch == on.Epoch + 2, "An older clock made a write Windows would ignore.");
        NightLightData offAgain = NightLightData.Parse(on.WithOn(false, now).ToBytes(now));
        Check(!offAgain.IsOn && offAgain.Body.Take(6).SequenceEqual(state.Body.Take(6)), "Switching off left other bytes changed.");
        Check(on.WithOn(true, now).IsOn && state.WithOn(false, now).Body.Count == state.Body.Count, "Setting the same state changed it.");

        NightLightData values = NightLightData.Parse(settings);
        Check(values.Kelvin == 3850 && NightLightData.Strength(values.Kelvin) == 50, "The strength was read wrong.");
        NightLightData neutral = NightLightData.Parse(values.WithKelvin(NightLightData.NeutralKelvin).ToBytes(now));
        Check(neutral.Kelvin == 6500 && NightLightData.Strength(neutral.Kelvin) == 0, "Strength 0 was not written.");
        NightLightData back = NightLightData.Parse(neutral.WithKelvin(3850).ToBytes(now));
        Check(back.Body.SequenceEqual(values.Body), "Restoring the strength did not restore the settings.");
        Check(NightLightData.Parse(values.WithKelvin(100).ToBytes(now)).Kelvin == 1200, "A strength outside Windows' range was written.");

        // Settings without a strength get one before the sunset time.
        byte[] noStrength = Hex("43 42 01 00 0A 02 01 00 2A 06 E1 93 87 D8 06 2A 2B 0E 22 43 42 01 00 02 01 C2 0A 00 CA 14 0E 17 00 CA 1E 0E 07 00 CA 32 0E 12 2E 2C 00 CA 3C 0E 06 2E 13 00 00 00 00 00");
        NightLightData plain = NightLightData.Parse(noStrength);
        Check(plain.Kelvin == NightLightData.DefaultKelvin, "A missing strength was not read as the default.");
        Check(NightLightData.Parse(plain.WithKelvin(3850).ToBytes(now)).Body.SequenceEqual(values.Body), "A missing strength was added in the wrong place.");

        Check(Fails(() => NightLightData.Parse([])), "Empty data was accepted.");
        Check(Fails(() => NightLightData.Parse(off.Take(off.Length - 1).ToArray())), "Cut-off data was accepted.");
        Check(Fails(() => NightLightData.Parse([.. off, 0])), "Data with extra bytes was accepted.");
        byte[] foreign = (byte[])off.Clone();
        foreign[0] = 0x44;
        Check(Fails(() => NightLightData.Parse(foreign)), "Data in another format was accepted.");

        Console.WriteLine("PASS: night light on/off, strength, missing strength, newer writes and unknown formats.");
    }
}
