﻿namespace Unosquare.FFplayDotNet.Console
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Unosquare.FFplayDotNet.Primitives;
    using Unosquare.Swan;

    class TestStreams
    {
        public const string BasePath = @"c:\users\unosp\Desktop\";

        public static string Mp4H264Regular = $"{BasePath}cowboys.mp4";

        public static string H264MulticastStream = @"udp://@225.1.1.181:5181/";

        public static string HlsMultiStream = @"http://qthttp.apple.com.edgesuite.net/1010qwoeiuryfg/sl.m3u8";

        /// <summary>
        /// Downloaded From: https://www.dropbox.com/sh/vggf640iniwxwyu/AABSeLJfAZeApEoJAY3N34Y2a?dl=0
        /// </summary>
        public static string MpegPart2 = $"{BasePath}big_buck_bunny_MPEG4.mp4";

        /// <summary>
        /// The mpg file form issue https://github.com/unosquare/ffmediaelement/issues/22
        /// </summary>
        public static string Mpg2 = $"{BasePath}22817BT_GTCTsang.mpg";

        /// <summary>
        /// The transport stream file
        /// From: https://github.com/unosquare/ffmediaelement/issues/16#issuecomment-299183167
        /// </summary>
        public static string TransportStreamFile = $"{BasePath}2013-12-18 22_45 - Anne Will.cut.ts";

        /// <summary>
        /// The matroska test. It contains various subtitle an audio tracks
        /// Files can be obtained here: https://sourceforge.net/projects/matroska/files/test_files/matroska_test_w1_1.zip/download
        /// </summary>
        public static string MatroskaTest = $"{BasePath}test5.mkv";
    }

    class Program
    {

        static void Main(string[] args)
        {
            var audioData = new List<byte>();
            var totalDurationSeconds = 0d;
            var totalBytes = 0;

            var player = new MediaContainer(TestStreams.Mp4H264Regular);

            player.OnVideoDataAvailable += (s, e) =>
            {
                totalBytes += e.BufferLength;
                totalDurationSeconds += e.Duration.TotalSeconds;

                var bytes = new byte[e.BufferLength];
                Marshal.Copy(e.Buffer, bytes, 0, e.BufferLength);

                $"Video PTS: {e.RenderTime}, DUR: {e.Duration} - Buffer: {e.BufferLength / 1024}KB".Info(typeof(Program));
            };

            player.OnAudioDataAvailable += (s, e) =>
            {
                totalBytes += e.BufferLength;
                var outputBytes = new byte[e.BufferLength];
                Marshal.Copy(e.Buffer, outputBytes, 0, outputBytes.Length);
                audioData.AddRange(outputBytes);
                $"Audio PTS: {e.RenderTime}, DUR: {e.Duration} - Buffer: {e.BufferLength / 1024}KB".Info(typeof(Program));
            };

            player.OnSubtitleDataAvailable += (s, e) =>
            {
                $"Subs PTS: {e.RenderTime}, DUR: {e.Duration}: {string.Join("; ", e.TextLines)}".Info(typeof(Program));
            };

            var startTime = DateTime.Now;
            var packetsToDecode = 100000;
            var packetsDecoded = 0;
            for (var i = 0; i < packetsToDecode; i++)
            {
                player.Process();
                if (player.IsAtEndOfFile || totalDurationSeconds >= 10d)
                {
                    "End of file reached or target decode limit met".Info(typeof(Program));
                    break;
                }

                packetsDecoded += 1;
            }

            ($"Took {DateTime.Now.Subtract(startTime).TotalSeconds} seconds to decode {packetsDecoded} packets, " +
                $"{player.Components.Video?.DecodedFrameCount} frames, {totalDurationSeconds} secs. {totalBytes / (1024 * 1024)}MB data.").Info(typeof(Program));
            var audioFile = @"c:\users\unosp\Desktop\output.wav";
            SaveWavFile(audioData, audioFile);
            Terminal.ReadKey(true, true);
        }

        private static void SaveWavFile(List<byte> audioData, string audioFile)
        {
            if (File.Exists(audioFile))
                File.Delete(audioFile);

            using (var file = File.OpenWrite(audioFile))
            {
                var bytesPerSample = 2;
                var spec = AudioComponentSpec.Output;
                using (var writer = new BinaryWriter(file))
                {
                    writer.Write("RIFF".ToCharArray()); // Group Id
                    writer.Write(0); // File Length (will be written later)
                    writer.Write("WAVE".ToCharArray()); // sRiffType
                    writer.Write("fmt ".ToCharArray()); // format chunk
                    writer.Write((uint)16); // the size of the header we just wrote (16 bytes)
                    writer.Write((ushort)1); // FormatTag (1 = MS PCM)
                    writer.Write((ushort)spec.ChannelCount); // channels
                    writer.Write((uint)spec.SampleRate); // sample rate
                    writer.Write((uint)(spec.SampleRate * spec.ChannelCount * bytesPerSample)); // nAvgBytesPerSec for buffer estimation samples * bytes per sample * channels
                    writer.Write((ushort)(bytesPerSample * spec.ChannelCount)); // nBlockAlign: block size is 2 bytes per sample times 2 channels
                    writer.Write((ushort)(bytesPerSample * 8)); // wBitsPerSample
                    writer.Write("data".ToCharArray()); // 
                    writer.Write((uint)audioData.Count); // this chunk size in bytes
                    writer.Write(audioData.ToArray());

                    // Set the total file length which is the byte count of the file minus the first 8 bytes
                    writer.Seek(4, SeekOrigin.Begin);
                    writer.Write((uint)(writer.BaseStream.Length - 8));
                }
            }
        }

    }
}
