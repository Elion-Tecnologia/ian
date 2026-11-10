using System.Buffers.Binary;
using Concentus.Enums;
using Concentus.Structs;

namespace Ian.Tests;

// Generated tones/silence only. No voice recordings or customer data in fixtures.
internal static class SyntheticOggOpus
{
    public static byte[] Create(int frames = 4, bool silent = false, int channels = 1, byte[]? packetOverride = null)
    {
#pragma warning disable CS0618
        var encoder = new OpusEncoder(48_000, channels, OpusApplication.OPUS_APPLICATION_AUDIO);
#pragma warning restore CS0618
        var header = new byte[19];
        "OpusHead"u8.CopyTo(header);
        header[8] = 1;
        header[9] = (byte)channels;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12), 48_000);
        var tags = new byte[16];
        "OpusTags"u8.CopyTo(tags);
        using var stream = new MemoryStream();
        stream.Write(Page(header, 0, 2, 0));
        stream.Write(Page(tags, 1, 0, 0));
        var pcm = new short[960 * channels];
        var packet = new byte[4000];
        for (var frame = 0; frame < frames; frame++)
        {
            for (var i = 0; i < pcm.Length; i++)
                pcm[i] = silent ? (short)0 : (short)(8000 * Math.Sin(2 * Math.PI * 440 * (frame * 960 + i / channels) / 48_000));
            var size = encoder.Encode(pcm.AsSpan(), 960, packet.AsSpan(), packet.Length);
            stream.Write(Page(packetOverride ?? packet[..size], (uint)frame + 2,
                frame == frames - 1 ? (byte)4 : (byte)0, (frame + 1) * 960));
        }
        return stream.ToArray();
    }

    public static byte[] Page(byte[] packet, uint sequence, byte flags, long granule)
    {
        var segments = packet.Length / 255 + 1;
        var page = new byte[27 + segments + packet.Length];
        "OggS"u8.CopyTo(page);
        page[5] = flags;
        BinaryPrimitives.WriteInt64LittleEndian(page.AsSpan(6), granule);
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(14), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(18), sequence);
        page[26] = (byte)segments;
        for (var segment = 0; segment < segments; segment++)
            page[27 + segment] = (byte)Math.Min(255, packet.Length - segment * 255);
        packet.CopyTo(page, 27 + segments);
        SetCrc(page);
        return page;
    }

    public static void SetCrc(Span<byte> page)
    {
        page.Slice(22, 4).Clear();
        uint crc = 0;
        foreach (var value in page)
        {
            crc ^= (uint)value << 24;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc << 1) ^ ((crc & 0x80000000) == 0 ? 0 : 0x04c11db7u);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(page[22..], crc);
    }
}
