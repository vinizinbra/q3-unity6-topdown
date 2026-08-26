using System.IO;
using System.Text;
using UnityEngine;

namespace Project.Audio.EditorTools
{
    // 16-bit PCM .wav encoding, shared by the split output and the temporary files handed to a
    // transcriber. Separate from AudioSilenceSplitter because the transcription path writes wavs
    // that have nothing to do with a segment being split (mono, 16kHz, thrown away immediately).
    internal static class WavUtility
    {
        // Headerless little-endian 16-bit PCM. What Google Speech-to-Text's LINEAR16 encoding
        // expects inline: it is told the sample rate and channel count in the request instead.
        internal static byte[] BuildPcm16(float[] samples)
        {
            byte[] bytes = new byte[samples.Length * 2];
            for (int i = 0; i < samples.Length; i++)
            {
                short value = (short)(Mathf.Clamp(samples[i], -1f, 1f) * short.MaxValue);
                bytes[i * 2] = (byte)(value & 0xFF);
                bytes[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
            }

            return bytes;
        }

        internal static byte[] Build(float[] samples, int channels, int frequency)
        {
            int byteCount = samples.Length * 2;

            using MemoryStream stream = new MemoryStream(44 + byteCount);
            using BinaryWriter writer = new BinaryWriter(stream, Encoding.ASCII);

            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + byteCount);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);                                // PCM header size
            writer.Write((short)1);                          // format: PCM
            writer.Write((short)channels);
            writer.Write(frequency);
            writer.Write(frequency * channels * 2);          // byte rate
            writer.Write((short)(channels * 2));             // block align
            writer.Write((short)16);                         // bits per sample
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(byteCount);

            for (int i = 0; i < samples.Length; i++)
            {
                writer.Write((short)(Mathf.Clamp(samples[i], -1f, 1f) * short.MaxValue));
            }

            writer.Flush();
            return stream.ToArray();
        }
    }
}
