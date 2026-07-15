namespace Mortz.Tools;

/// <summary>Deterministic placeholder audio, regenerated in place until real
/// sounds exist.</summary>
internal static class GenSounds
{
    private const int RATE = 44_100;
    private const int SEED = 0x4D5A;

    public static void Run()
    {
        string output = Path.Combine(Program.RepoRoot(), "Assets", "Sounds");
        Directory.CreateDirectory(output);
        Random random = new(SEED);

        Write(output, "mortar_fire.wav", Finite(0.28, (t, _) =>
            0.66 * Math.Sin(2 * Math.PI * (110 * t - 115 * t * t)) * Math.Exp(-10 * t) +
            0.25 * Noise(random) * Math.Exp(-45 * t)));
        Write(output, "shell_whoosh.wav", PeriodicNoise(0.6, random), loop: true);
        Write(output, "shell_impact.wav", Finite(0.34, (t, _) =>
            0.72 * Math.Sin(2 * Math.PI * 64 * t) * Math.Exp(-13 * t) +
            0.34 * Noise(random) * Math.Exp(-55 * t)));
        Write(output, "parry_raise.wav", Finite(0.19, (t, _) =>
            0.42 * Math.Sin(2 * Math.PI * (220 * t + 950 * t * t)) *
            Math.Sin(Math.PI * t / 0.19)));
        Write(output, "parry_success.wav", Finite(0.27, (t, _) =>
            (0.28 * Math.Sin(2 * Math.PI * 1240 * t) +
             0.22 * Math.Sin(2 * Math.PI * 1810 * t)) * Math.Exp(-10 * t) +
            0.18 * Noise(random) * Math.Exp(-65 * t)));
        Write(output, "regular_kill.wav", Finite(0.42, (t, _) =>
            0.34 * Math.Sin(2 * Math.PI * KillTonePhase(t)) * Math.Exp(-2.5 * t)));
        Write(output, "owned.wav", Finite(0.62, (t, _) =>
            0.43 * Math.Sin(2 * Math.PI * (530 * t - 250 * t * t)) * Math.Exp(-2.8 * t) +
            0.12 * Math.Sin(2 * Math.PI * (265 * t - 125 * t * t)) * Math.Exp(-2.8 * t)));

        Console.WriteLine($"generated 7 placeholder sounds in {output}");
    }

    private static double[] Finite(double seconds, Func<double, int, double> sample)
    {
        int count = (int)(seconds * RATE);
        double[] result = new double[count];
        int fade = (int)(0.008 * RATE);
        for (int i = 0; i < count; i++)
        {
            double gain = i >= count - fade ? (double)(count - 1 - i) / fade : 1;
            result[i] = sample((double)i / RATE, i) * Math.Max(0, gain);
        }
        return result;
    }

    /// <summary>A Fourier noise bed uses whole cycles, making the loop join exact.</summary>
    private static double[] PeriodicNoise(double seconds, Random random)
    {
        int count = (int)(seconds * RATE);
        double[] phases = Enumerable.Range(0, 48)
            .Select(_ => random.NextDouble() * 2 * Math.PI).ToArray();
        double[] amplitudes = Enumerable.Range(0, phases.Length)
            .Select(i => 1.0 / Math.Sqrt(i + 3)).ToArray();
        double[] result = new double[count];
        for (int i = 0; i < count; i++)
        {
            double phase = 2 * Math.PI * i / count;
            double value = 0;
            for (int band = 0; band < phases.Length; band++)
                value += amplitudes[band] * Math.Sin((band + 3) * phase + phases[band]);
            result[i] = value * 0.045;
        }
        return result;
    }

    private static double Noise(Random random) => random.NextDouble() * 2 - 1;

    private static double KillTonePhase(double t) => t switch
    {
        < 0.14 => 440 * t,
        < 0.28 => 440 * 0.14 + 554 * (t - 0.14),
        _ => 440 * 0.14 + 554 * 0.14 + 659 * (t - 0.28),
    };

    private static void Write(string directory, string name, double[] samples, bool loop = false)
    {
        string path = Path.Combine(directory, name);
        using FileStream stream = File.Create(path);
        using BinaryWriter writer = new(stream);
        int dataBytes = samples.Length * sizeof(short);
        int smplBytes = loop ? 8 + 60 : 0;
        writer.Write("RIFF"u8.ToArray());
        writer.Write(4 + (8 + 16) + (8 + dataBytes) + smplBytes);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(RATE);
        writer.Write(RATE * sizeof(short));
        writer.Write((short)sizeof(short));
        writer.Write((short)16);
        writer.Write("data"u8.ToArray());
        writer.Write(dataBytes);
        foreach (double sample in samples)
            writer.Write((short)Math.Clamp((int)Math.Round(sample * short.MaxValue),
                short.MinValue, short.MaxValue));
        if (loop)
            WriteLoopChunk(writer, samples.Length);
    }

    private static void WriteLoopChunk(BinaryWriter writer, int sampleCount)
    {
        writer.Write("smpl"u8.ToArray());
        writer.Write(60);
        writer.Write(0u); // manufacturer
        writer.Write(0u); // product
        writer.Write((uint)(1_000_000_000L / RATE));
        writer.Write(60u); // MIDI unity note
        writer.Write(0u); // pitch fraction
        writer.Write(0u); // SMPTE format
        writer.Write(0u); // SMPTE offset
        writer.Write(1u); // loop count
        writer.Write(0u); // sampler data
        writer.Write(0u); // cue point id
        writer.Write(0u); // forward loop
        writer.Write(0u); // start
        writer.Write((uint)(sampleCount - 1));
        writer.Write(0u); // fraction
        writer.Write(0u); // repeat forever
    }
}
