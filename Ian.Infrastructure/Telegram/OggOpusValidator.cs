using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Concentus.Structs;
using Ian.Application.Telegram;

namespace Ian.Infrastructure.Telegram;

// Restricted RFC 3533/7845 profile: one complete, unchained mono/stereo Opus stream.
// Decode actual packets; neither Telegram duration nor a forged granule is authoritative.
internal static class OggOpusValidator
{
    public static TelegramVoiceFailure? Validate(
        ReadOnlySpan<byte> audio, int maxSeconds, CancellationToken cancellationToken)
    {
        var packet = new byte[65_536];
        var pcm = new short[5760 * 2];
        OpusDecoder? decoder = null;
        try
        {
            var offset = 0;
            var packetLength = 0;
            var packetIndex = 0;
            var sequence = 0u;
            var serial = 0u;
            var channels = 0;
            var preSkip = 0;
            long decoded = 0;
            var lastSamples = 0;
            var audible = false;
            var ended = false;
            while (offset < audio.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = audio[offset..];
                if (ended || page.Length < 27 || !page[..4].SequenceEqual("OggS"u8) || page[4] != 0)
                    return TelegramVoiceFailure.InvalidFile;
                var flags = page[5];
                var segments = page[26];
                if ((flags & ~7) != 0 || page.Length < 27 + segments || segments == 0 ||
                    ((flags & 1) != 0) != (packetLength > 0) || ((flags & 2) != 0) != (sequence == 0))
                    return TelegramVoiceFailure.InvalidFile;
                var pageSerial = BinaryPrimitives.ReadUInt32LittleEndian(page[14..]);
                if (sequence == 0) serial = pageSerial;
                if (pageSerial != serial || BinaryPrimitives.ReadUInt32LittleEndian(page[18..]) != sequence++)
                    return TelegramVoiceFailure.InvalidFile;
                var bodyLength = 0;
                foreach (var length in page.Slice(27, segments)) bodyLength += length;
                var pageLength = 27 + segments + bodyLength;
                if (page.Length < pageLength || !ValidCrc(page[..pageLength]))
                    return TelegramVoiceFailure.InvalidFile;
                var bodyOffset = 27 + segments;
                var completed = false;
                var hadAudio = false;
                foreach (var length in page.Slice(27, segments))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (packetLength + length > packet.Length) return TelegramVoiceFailure.InvalidFile;
                    page.Slice(bodyOffset, length).CopyTo(packet.AsSpan(packetLength));
                    bodyOffset += length;
                    packetLength += length;
                    if (length == 255) continue;
                    completed = true;
                    var data = packet.AsSpan(0, packetLength);
                    if (packetIndex == 0)
                    {
                        if (data.Length != 19 || !data[..8].SequenceEqual("OpusHead"u8) ||
                            data[8] != 1 || data[9] is not (1 or 2) || data[18] != 0 ||
                            (flags & 2) == 0 || segments != 1)
                            return TelegramVoiceFailure.InvalidFile;
                        channels = data[9];
                        preSkip = BinaryPrimitives.ReadUInt16LittleEndian(data[10..]);
                        // Explicit managed implementation: no optional native library discovery.
#pragma warning disable CS0618
                        decoder = new OpusDecoder(48_000, channels);
#pragma warning restore CS0618
                        decoder.Gain = BinaryPrimitives.ReadInt16LittleEndian(data[16..]);
                    }
                    else if (packetIndex == 1)
                    {
                        if (!ValidTags(data)) return TelegramVoiceFailure.InvalidFile;
                    }
                    else
                    {
                        if (data.Length == 0 || decoder is null) return TelegramVoiceFailure.InvalidFile;
                        lastSamples = decoder.Decode(data, pcm.AsSpan(), 5760, false);
                        if (lastSamples <= 0) return TelegramVoiceFailure.InvalidFile;
                        decoded += lastSamples;
                        // Bound decoder work even if the final granule claims a short duration.
                        if (decoded > (long)maxSeconds * 48_000 + preSkip + 5760)
                            return TelegramVoiceFailure.DurationLimitExceeded;
                        for (var sample = 0; sample < lastSamples; sample++)
                        {
                            if (decoded - lastSamples + sample < preSkip) continue;
                            for (var channel = 0; channel < channels; channel++)
                                audible |= Math.Abs((int)pcm[sample * channels + channel]) > 32;
                        }
                        hadAudio = true;
                    }
                    CryptographicOperations.ZeroMemory(data);
                    Array.Clear(pcm);
                    packetLength = 0;
                    packetIndex++;
                }
                var granule = BinaryPrimitives.ReadInt64LittleEndian(page[6..]);
                ended = (flags & 4) != 0;
                if (!completed && granule != -1 || completed && !hadAudio && granule != 0 ||
                    hadAudio && !ended && granule != decoded)
                    return TelegramVoiceFailure.InvalidFile;
                if (ended)
                {
                    if (packetLength != 0 || packetIndex < 3 || !hadAudio ||
                        granule <= preSkip || granule > decoded || granule < decoded - lastSamples)
                        return TelegramVoiceFailure.InvalidFile;
                    if (granule - preSkip > (long)maxSeconds * 48_000)
                        return TelegramVoiceFailure.DurationLimitExceeded;
                }
                offset += pageLength;
            }
            if (!ended) return TelegramVoiceFailure.InvalidFile;
            return audible ? null : TelegramVoiceFailure.EmptyTranscription;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Decoder errors and malformed containers never leak payloads or diagnostic bodies.
            return TelegramVoiceFailure.InvalidFile;
        }
        finally
        {
            try { decoder?.ResetState(); }
            finally
            {
                CryptographicOperations.ZeroMemory(packet);
                CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(pcm.AsSpan()));
            }
        }
    }

    private static bool ValidTags(ReadOnlySpan<byte> data)
    {
        if (data.Length < 16 || !data[..8].SequenceEqual("OpusTags"u8)) return false;
        var vendorLength = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
        if (vendorLength > data.Length - 16) return false;
        var offset = 12 + (int)vendorLength;
        var count = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
        offset += 4;
        if (count > (data.Length - offset) / 4) return false;
        for (var index = 0u; index < count; index++)
        {
            if (data.Length - offset < 4) return false;
            var size = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
            offset += 4;
            if (size > data.Length - offset) return false;
            offset += (int)size;
        }
        return true;
    }

    private static bool ValidCrc(ReadOnlySpan<byte> page)
    {
        uint crc = 0;
        for (var index = 0; index < page.Length; index++)
        {
            crc ^= (uint)(index is >= 22 and < 26 ? 0 : page[index]) << 24;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc << 1) ^ ((crc & 0x80000000) != 0 ? 0x04c11db7u : 0);
        }
        return crc == BinaryPrimitives.ReadUInt32LittleEndian(page[22..]);
    }
}
