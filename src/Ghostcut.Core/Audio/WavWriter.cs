using System.Buffers.Binary;

namespace Ghostcut.Core.Audio;

/// <summary>Writes the timeline mix as a 32-bit float WAV (input for the export).</summary>
public static class WavWriter
{
    private const int BlockFrames = 8192;

    public static void RenderMix(AudioMixer mixer, long frameCount, Stream output, IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        WriteHeader(output, frameCount);
        var buffer = new float[BlockFrames * AudioFormat.Channels];
        var bytes = new byte[buffer.Length * sizeof(float)];
        for (long frame = 0; frame < frameCount; frame += BlockFrames)
        {
            cancel.ThrowIfCancellationRequested();
            var frames = (int)Math.Min(BlockFrames, frameCount - frame);
            var span = buffer.AsSpan(0, frames * AudioFormat.Channels);
            mixer.Mix(frame, span);
            for (var i = 0; i < span.Length; i++)
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4), span[i]);
            output.Write(bytes, 0, span.Length * 4);
            progress?.Report((double)(frame + frames) / Math.Max(1, frameCount));
        }
    }

    public static void WriteHeader(Stream output, long frameCount)
    {
        const short formatFloat = 3;
        const short bitsPerSample = 32;
        var blockAlign = (short)(AudioFormat.Channels * bitsPerSample / 8);
        var dataBytes = frameCount * blockAlign;
        if (dataBytes > uint.MaxValue - 36)
            throw new InvalidOperationException("Mix too long for a WAV file.");
        var header = new byte[44];
        var h = header.AsSpan();
        "RIFF"u8.CopyTo(h);
        BinaryPrimitives.WriteUInt32LittleEndian(h[4..], (uint)(36 + dataBytes));
        "WAVE"u8.CopyTo(h[8..]);
        "fmt "u8.CopyTo(h[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(h[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(h[20..], formatFloat);
        BinaryPrimitives.WriteInt16LittleEndian(h[22..], (short)AudioFormat.Channels);
        BinaryPrimitives.WriteInt32LittleEndian(h[24..], AudioFormat.SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(h[28..], AudioFormat.SampleRate * blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(h[32..], blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(h[34..], bitsPerSample);
        "data"u8.CopyTo(h[36..]);
        BinaryPrimitives.WriteUInt32LittleEndian(h[40..], (uint)dataBytes);
        output.Write(header);
    }
}
