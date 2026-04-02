namespace Screamsaver.Core;

/// <summary>
/// Pure audio level math — no NAudio or OS dependencies, fully unit-testable.
/// </summary>
public static class AudioLevelCalculator
{
    /// <summary>Converts an RMS linear amplitude [0..1] to dBFS.</summary>
    public static double ToDbFs(double rms) =>
        rms <= 0 ? double.NegativeInfinity : 20.0 * Math.Log10(rms);

    /// <summary>RMS of interleaved 32-bit IEEE float samples.</summary>
    public static double ComputeRmsFloat32(byte[] buffer, int bytesRecorded)
    {
        int samples = bytesRecorded / 4;
        if (samples == 0) return 0;
        double sum = 0;
        for (int i = 0; i < samples; i++)
        {
            float s = BitConverter.ToSingle(buffer, i * 4);
            sum += s * s;
        }
        return Math.Sqrt(sum / samples);
    }

    /// <summary>RMS of interleaved 16-bit signed PCM samples, normalised to [0..1].</summary>
    public static double ComputeRmsPcm16(byte[] buffer, int bytesRecorded)
    {
        int samples = bytesRecorded / 2;
        if (samples == 0) return 0;
        double sum = 0;
        for (int i = 0; i < samples; i++)
        {
            double normalised = BitConverter.ToInt16(buffer, i * 2) / 32768.0;
            sum += normalised * normalised;
        }
        return Math.Sqrt(sum / samples);
    }
}
