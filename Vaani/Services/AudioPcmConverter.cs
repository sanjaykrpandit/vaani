using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Vaani.Services;

internal static class AudioPcmConverter
{
    public static byte[] ToTarget16kHz(byte[] buffer, int bytesRecorded, WaveFormat sourceFormat)
    {
        if (sourceFormat.SampleRate == 16000 &&
            sourceFormat.BitsPerSample == 16 &&
            sourceFormat.Channels == 1 &&
            sourceFormat.Encoding == WaveFormatEncoding.Pcm)
        {
            var copy = new byte[bytesRecorded];
            Array.Copy(buffer, copy, bytesRecorded);
            return copy;
        }

        using var ms = new MemoryStream(buffer, 0, bytesRecorded);
        ISampleProvider samples = new RawSourceWaveStream(ms, sourceFormat).ToSampleProvider();
        if (samples.WaveFormat.Channels == 2)
            samples = new StereoToMonoSampleProvider(samples);
        if (samples.WaveFormat.SampleRate != 16000)
            samples = new WdlResamplingSampleProvider(samples, 16000);

        var pcm16 = samples.ToWaveProvider16();
        using var output = new MemoryStream();
        var readBuffer = new byte[4096];
        int read;
        while ((read = pcm16.Read(readBuffer, 0, readBuffer.Length)) > 0)
            output.Write(readBuffer, 0, read);

        return output.ToArray();
    }
}
