using Screamsaver.Core;

namespace Screamsaver.Tests.Core;

public class AudioLevelCalculatorTests
{
    // ── ToDbFs ────────────────────────────────────────────────────────────────

    [Fact]
    public void ToDbFs_FullScale_ReturnsZero()
    {
        Assert.Equal(0.0, AudioLevelCalculator.ToDbFs(1.0), precision: 6);
    }

    [Fact]
    public void ToDbFs_HalfAmplitude_ReturnsMinus6dB()
    {
        // 20 * log10(0.5) ≈ -6.02 dB
        Assert.Equal(-6.02, AudioLevelCalculator.ToDbFs(0.5), precision: 1);
    }

    [Fact]
    public void ToDbFs_Zero_ReturnsNegativeInfinity()
    {
        Assert.Equal(double.NegativeInfinity, AudioLevelCalculator.ToDbFs(0.0));
    }

    [Fact]
    public void ToDbFs_Negative_ReturnsNegativeInfinity()
    {
        Assert.Equal(double.NegativeInfinity, AudioLevelCalculator.ToDbFs(-1.0));
    }

    // ── ComputeRmsFloat32 ─────────────────────────────────────────────────────

    [Fact]
    public void ComputeRmsFloat32_Silence_ReturnsZero()
    {
        var buffer = new byte[16]; // all zeros → all samples 0.0f
        Assert.Equal(0.0, AudioLevelCalculator.ComputeRmsFloat32(buffer, 16));
    }

    [Fact]
    public void ComputeRmsFloat32_FullScaleSine_ApproachesPointSevenOne()
    {
        // A sine wave at full scale has RMS = 1/√2 ≈ 0.7071
        const int samples = 1024;
        var buffer = new byte[samples * 4];
        for (int i = 0; i < samples; i++)
        {
            float v = (float)Math.Sin(2 * Math.PI * i / samples);
            BitConverter.TryWriteBytes(buffer.AsSpan(i * 4), v);
        }
        var rms = AudioLevelCalculator.ComputeRmsFloat32(buffer, buffer.Length);
        Assert.Equal(1.0 / Math.Sqrt(2), rms, precision: 2);
    }

    [Fact]
    public void ComputeRmsFloat32_ConstantOne_ReturnsOne()
    {
        const int samples = 8;
        var buffer = new byte[samples * 4];
        for (int i = 0; i < samples; i++)
            BitConverter.TryWriteBytes(buffer.AsSpan(i * 4), 1.0f);
        Assert.Equal(1.0, AudioLevelCalculator.ComputeRmsFloat32(buffer, buffer.Length), precision: 6);
    }

    [Fact]
    public void ComputeRmsFloat32_EmptyBuffer_ReturnsZero()
    {
        Assert.Equal(0.0, AudioLevelCalculator.ComputeRmsFloat32(new byte[8], 0));
    }

    // ── ComputeRmsPcm16 ───────────────────────────────────────────────────────

    [Fact]
    public void ComputeRmsPcm16_Silence_ReturnsZero()
    {
        var buffer = new byte[16];
        Assert.Equal(0.0, AudioLevelCalculator.ComputeRmsPcm16(buffer, 16));
    }

    [Fact]
    public void ComputeRmsPcm16_FullScalePositive_ReturnsApproximatelyOne()
    {
        // 32767 / 32768 ≈ 0.99997
        const int samples = 4;
        var buffer = new byte[samples * 2];
        for (int i = 0; i < samples; i++)
            BitConverter.TryWriteBytes(buffer.AsSpan(i * 2), (short)32767);
        var rms = AudioLevelCalculator.ComputeRmsPcm16(buffer, buffer.Length);
        Assert.Equal(32767.0 / 32768.0, rms, precision: 4);
    }

    [Fact]
    public void ComputeRmsPcm16_EmptyBuffer_ReturnsZero()
    {
        Assert.Equal(0.0, AudioLevelCalculator.ComputeRmsPcm16(new byte[8], 0));
    }

    [Fact]
    public void ComputeRmsPcm16_FullScaleSine_ApproachesPointSevenOne()
    {
        const int samples = 1024;
        var buffer = new byte[samples * 2];
        for (int i = 0; i < samples; i++)
        {
            var v = (short)(32767 * Math.Sin(2 * Math.PI * i / samples));
            BitConverter.TryWriteBytes(buffer.AsSpan(i * 2), v);
        }
        var rms = AudioLevelCalculator.ComputeRmsPcm16(buffer, buffer.Length);
        Assert.Equal(1.0 / Math.Sqrt(2), rms, precision: 2);
    }

    // ── Integration: ToDbFs(ComputeRms) threshold logic ───────────────────────

    [Theory]
    [InlineData(-20.0, true)]   // loud enough to trigger at -20 dBFS threshold
    [InlineData(-10.0, false)]  // too quiet to trigger at -10 dBFS threshold
    public void ThresholdLogic_Float32_TriggersCorrectly(double thresholdDb, bool shouldTrigger)
    {
        // Build a constant-amplitude float32 buffer at RMS ≈ 0.1 (= -20 dBFS)
        const float amplitude = 0.1f;
        const int samples = 256;
        var buffer = new byte[samples * 4];
        for (int i = 0; i < samples; i++)
            BitConverter.TryWriteBytes(buffer.AsSpan(i * 4), amplitude);

        var rms = AudioLevelCalculator.ComputeRmsFloat32(buffer, buffer.Length);
        var dBfs = AudioLevelCalculator.ToDbFs(rms);
        Assert.Equal(shouldTrigger, dBfs >= thresholdDb);
    }
}
