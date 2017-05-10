﻿namespace Unosquare.FFplayDotNet
{
    using FFmpeg.AutoGen;
    using System;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Unosquare.FFplayDotNet.Core;
    using Unosquare.Swan;

    /// <summary>
    /// Represents a media component of a given media type within a 
    /// media container. Derived classes must implement frame handling
    /// logic.
    /// </summary>
    /// <seealso cref="System.IDisposable" />
    public unsafe abstract class MediaComponent : IDisposable
    {

        #region Constants

        /// <summary>
        /// Contains constants defining dictionary entry names for codec options
        /// </summary>
        protected static class CodecOption
        {
            public const string Threads = "threads";
            public const string RefCountedFrames = "refcounted_frames";
            public const string LowRes = "lowres";
        }

        #endregion

        #region Private Declarations

        /// <summary>
        /// Detects redundant, unmanaged calls to the Dispose method.
        /// </summary>
        private bool IsDisposed = false;

        /// <summary>
        /// Holds a reference to the Codec Context.
        /// </summary>
        internal AVCodecContext* CodecContext;

        /// <summary>
        /// Holds a reference to the associated input context stream
        /// </summary>
        internal AVStream* Stream;

        /// <summary>
        /// Contains the packets pending to be sent to the decoder
        /// </summary>
        private readonly PacketQueue Packets = new PacketQueue();

        /// <summary>
        /// The packets that have been sent to the decoder. We keep track of them in order to dispose them
        /// once a frame has been decoded.
        /// </summary>
        private readonly PacketQueue SentPackets = new PacketQueue();

        /// <summary>
        /// Contains the frames that have been decoded for this media component.
        /// These frames have to be dequeued.
        /// </summary>
        //private readonly FrameQueue Frames = new FrameQueue();

        #endregion

        #region Properties

        /// <summary>
        /// Gets the type of the media.
        /// </summary>
        public MediaType MediaType { get; }

        /// <summary>
        /// Gets the media container associated with this component.
        /// </summary>
        public MediaContainer Container { get; }

        /// <summary>
        /// Gets the index of the associated stream.
        /// </summary>
        public int StreamIndex { get; }

        /// <summary>
        /// Gets the start time of this stream component.
        /// If there is no such information it will return TimeSpan.MinValue
        /// </summary>
        public TimeSpan StartTime { get; }

        /// <summary>
        /// Gets the duration of this stream component.
        /// If there is no such information it will return TimeSpan.MinValue
        /// </summary>
        public TimeSpan Duration { get; }

        /// <summary>
        /// Gets the end time of this stream component.
        /// If there is no such information it will return TimeSpan.MinValue
        /// </summary>
        public TimeSpan EndTime { get; }

        /// <summary>
        /// Gets the number of frames that have been decoded by this component
        /// </summary>
        public ulong DecodedFrameCount { get; private set; }

        /// <summary>
        /// Gets the time in UTC at which the last frame was processed.
        /// </summary>
        public DateTime LastProcessedTimeUTC { get; protected set; }

        /// <summary>
        /// Gets the render (start) time of the last processed frame.
        /// </summary>
        public TimeSpan LastFrameTime { get; protected set; }

        /// <summary>
        /// Gets the render time or the last packet that was received by this media component.
        /// </summary>
        public TimeSpan LastPacketTime { get; protected set; }

        /// <summary>
        /// Gets the number of packets that have been received
        /// by this media component.
        /// </summary>
        public ulong ReceivedPacketCount { get; private set; }

        /// <summary>
        /// Gets the current length in bytes of the 
        /// packet buffer.
        /// </summary>
        public int PacketBufferLength { get { return Packets.BufferLength; } }

        /// <summary>
        /// Gets the current duration of the packet buffer.
        /// </summary>
        public TimeSpan PacketBufferDuration
        {
            get
            {
                return Packets.Duration.ToTimeSpan(Stream->time_base);
            }
        }

        /// <summary>
        /// Gets the number of packets in the queue. These packets
        /// are continuously fed into the decoder.
        /// </summary>
        public int PacketBufferCount { get { return Packets.Count; } }

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes a new instance of the <see cref="MediaComponent"/> class.
        /// </summary>
        /// <param name="container">The container.</param>
        /// <param name="streamIndex">Index of the stream.</param>
        /// <exception cref="System.ArgumentNullException">container</exception>
        /// <exception cref="System.Exception"></exception>
        protected MediaComponent(MediaContainer container, int streamIndex)
        {
            // NOTE: code largely based on stream_component_open
            Container = container ?? throw new ArgumentNullException(nameof(container));
            CodecContext = ffmpeg.avcodec_alloc_context3(null);
            StreamIndex = streamIndex;
            Stream = container.InputContext->streams[StreamIndex];

            // Set codec options
            var setCodecParamsResult = ffmpeg.avcodec_parameters_to_context(CodecContext, Stream->codecpar);

            if (setCodecParamsResult < 0)
                $"Could not set codec parameters. Error code: {setCodecParamsResult}".Warn(typeof(MediaContainer));

            // We set the packet timebase in the same timebase as the stream as opposed to the tpyical AV_TIME_BASE
            ffmpeg.av_codec_set_pkt_timebase(CodecContext, Stream->time_base);

            // Find the codec and set it.
            var codec = ffmpeg.avcodec_find_decoder(Stream->codec->codec_id);
            if (codec == null)
            {
                var errorMessage = $"Fatal error. Unable to find suitable decoder for {Stream->codec->codec_id.ToString()}";
                Dispose();
                throw new MediaContainerException(errorMessage);
            }

            CodecContext->codec_id = codec->id;

            // Process the low res index option
            var lowResIndex = ffmpeg.av_codec_get_max_lowres(codec);
            if (Container.Options.EnableLowRes)
            {
                ffmpeg.av_codec_set_lowres(CodecContext, lowResIndex);
                CodecContext->flags |= ffmpeg.CODEC_FLAG_EMU_EDGE;
            }
            else
            {
                lowResIndex = 0;
            }

            // Configure the codec context flags
            if (Container.Options.EnableFastDecoding) CodecContext->flags2 |= ffmpeg.AV_CODEC_FLAG2_FAST;
            if ((codec->capabilities & ffmpeg.AV_CODEC_CAP_DR1) != 0) CodecContext->flags |= ffmpeg.CODEC_FLAG_EMU_EDGE;
            if ((codec->capabilities & ffmpeg.AV_CODEC_CAP_TRUNCATED) != 0) CodecContext->flags |= ffmpeg.AV_CODEC_CAP_TRUNCATED;
            if ((codec->capabilities & ffmpeg.CODEC_FLAG2_CHUNKS) != 0) CodecContext->flags |= ffmpeg.CODEC_FLAG2_CHUNKS;

            // Setup additional settings. The most important one is Threads -- Setting it to 1 decoding is very slow. Setting it to auto
            // decoding is very fast in most scenarios.
            var codecOptions = Container.Options.CodecOptions.FilterOptions(CodecContext->codec_id, Container.InputContext, Stream, codec);
            if (codecOptions.HasKey(CodecOption.Threads) == false) codecOptions[CodecOption.Threads] = "auto";
            if (lowResIndex != 0) codecOptions[CodecOption.LowRes] = lowResIndex.ToString(CultureInfo.InvariantCulture);
            if (CodecContext->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO || CodecContext->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO)
                codecOptions[CodecOption.RefCountedFrames] = 1.ToString(CultureInfo.InvariantCulture);

            // Open the CodecContext
            var codecOpenResult = ffmpeg.avcodec_open2(CodecContext, codec, codecOptions.Reference);
            if (codecOpenResult < 0)
            {
                Dispose();
                throw new MediaContainerException($"Unable to open codec. Error code {codecOpenResult}");
            }

            // If there are any codec options left over from passing them, it means they were not consumed
            if (codecOptions.First() != null)
                $"Codec Option '{codecOptions.First().Key}' not found.".Warn(typeof(MediaContainer));

            // Startup done. Set some options.
            Stream->discard = AVDiscard.AVDISCARD_DEFAULT;
            MediaType = (MediaType)CodecContext->codec_type;

            // Compute the start time
            if (Stream->start_time == ffmpeg.AV_NOPTS)
                StartTime = Container.InputContext->start_time.ToTimeSpan();
            else
                StartTime = Stream->start_time.ToTimeSpan(Stream->time_base);

            // compute the duration
            if (Stream->duration == ffmpeg.AV_NOPTS || Stream->duration == 0)
                Duration = Container.InputContext->duration.ToTimeSpan();
            else
                Duration = Stream->duration.ToTimeSpan(Stream->time_base);

            // compute the end time
            if (StartTime != TimeSpan.MinValue && Duration != TimeSpan.MinValue)
                EndTime = StartTime + Duration;
            else
                EndTime = TimeSpan.MinValue;

            $"{MediaType}: Start Time: {StartTime}; End Time: {EndTime}; Duration: {Duration}".Trace(typeof(MediaContainer));

        }

        #endregion

        #region Methods

        /// <summary>
        /// Determines whether the specified packet is a Null Packet (data = null, size = 0)
        /// These null packets are used to read multiple frames from a single packet.
        /// </summary>
        protected bool IsEmptyPacket(AVPacket* packet)
        {
            if (packet == null) return false;
            return (packet->data == null && packet->size == 0);
        }

        /// <summary>
        /// Clears the pending and sent Packet Queues releasing all memory held by those packets.
        /// Additionally it flushes the codec buffered packets.
        /// </summary>
        internal void ClearPacketQueues()
        {
            // Release packets that are already in the queue.
            SentPackets.Clear();
            Packets.Clear();

            // Discard any data that was buffered in codec's internal memory.
            // reset the buffer
            if (CodecContext != null)
                ffmpeg.avcodec_flush_buffers(CodecContext);
        }

        /// <summary>
        /// Sends a special kind of packet (an empty packet)
        /// that tells the decoder to enter draining mode.
        /// </summary>
        internal void SendEmptyPacket()
        {
            var emptyPacket = ffmpeg.av_packet_alloc();
            emptyPacket->data = null;
            emptyPacket->size = 0;
            SendPacket(emptyPacket);
        }

        /// <summary>
        /// Pushes a packet into the decoding Packet Queue
        /// and processes the packet in order to try to decode
        /// 1 or more frames. The packet has to be within the range of
        /// the start time and end time of 
        /// </summary>
        /// <param name="packet">The packet.</param>
        internal void SendPacket(AVPacket* packet)
        {
            // TODO: check if packet is in play range
            // ffplay.c reference: pkt_in_play_range
            if (packet == null) return;

            Packets.Push(packet);
            if (IsEmptyPacket(packet) == false)
                LastPacketTime = packet->pts.ToTimeSpan(Stream->time_base);

            ReceivedPacketCount += 1;
        }

        /// <summary>
        /// Decodes the next packet in the packet queue in this media component.
        /// </summary>
        /// <returns></returns>
        public int DecodeNextPacket()
        {
            if (PacketBufferCount <= 0) return 0;
            var decodedFrames = DecodeNextPacketInternal();
            DecodedFrameCount += (ulong)decodedFrames;
            return decodedFrames;
        }

        /// <summary>
        /// Receives 0 or more frames from the next available packet in the Queue.
        /// This sends the first available packet to dequeue to the decoder
        /// and uses the decoded frames (if any) to their corresponding
        /// ProcessFrame method.
        /// </summary>
        /// <returns></returns>
        private int DecodeNextPacketInternal()
        {
            // Ensure there is at least one packet in the queue
            if (PacketBufferCount <= 0) return 0;

            // Setup some initial state variables
            var packet = Packets.Peek();
            var receiveFrameResult = 0;
            var receivedFrameCount = 0;

            if (MediaType == MediaType.Audio || MediaType == MediaType.Video)
            {
                // If it's audio or video, we use the new API and the decoded frames are stored in AVFrame
                // Let us send the packet to the codec for decoding a frame of uncompressed data later
                var sendPacketResult = ffmpeg.avcodec_send_packet(CodecContext, IsEmptyPacket(packet) ? null : packet);

                // Let's check and see if we can get 1 or more frames from the packet we just sent to the decoder.
                // Audio packets will typically contain 1 or more audioframes
                // Video packets might require several packets to decode 1 frame
                while (receiveFrameResult == 0)
                {
                    // Allocate a frame in unmanaged memory and 
                    // Try to receive the decompressed frame data
                    var outputFrame = ffmpeg.av_frame_alloc();
                    receiveFrameResult = ffmpeg.avcodec_receive_frame(CodecContext, outputFrame);

                    try
                    {
                        // Process the output frame if we were successful on a different thread if possible
                        // That is, using a new task
                        if (receiveFrameResult == 0)
                        {
                            // Send the frame to processing
                            receivedFrameCount += 1;
                            ProcessFrame(outputFrame);
                        }
                    }
                    finally
                    {

                        // Release the frame as the decoded data has been processed 
                        // regardless if there was any output.
                        ffmpeg.av_frame_free(&outputFrame);
                    }
                }
            }
            else if (MediaType == MediaType.Subtitle)
            {
                // Fors subtitles we use the old API (new API send_packet/receive_frame) is not yet available
                var gotFrame = 0;
                var outputFrame = new AVSubtitle(); // We create the struct in managed memory as there is no API to create a subtitle.
                receiveFrameResult = ffmpeg.avcodec_decode_subtitle2(CodecContext, &outputFrame, &gotFrame, packet);

                // Check if there is an error decoding the packet.
                // If there is, remove the packet clear the sent packets
                if (receiveFrameResult < 0)
                {
                    ffmpeg.avsubtitle_free(&outputFrame);
                    SentPackets.Clear();
                    $"{MediaType}: Error decoding. Error Code: {receiveFrameResult}".Error(typeof(MediaContainer));
                }
                else
                {
                    // Process the first frame if we got it from the packet
                    // Note that there could be more frames (subtitles) in the packet
                    if (gotFrame != 0)
                    {
                        // Send the frame to processing
                        receivedFrameCount += 1;
                        ProcessFrame(&outputFrame);
                    }

                    // Once processed, we don't need it anymore. Release it.
                    ffmpeg.avsubtitle_free(&outputFrame);

                    // Let's check if we have more decoded frames from the same single packet
                    // Packets may contain more than 1 frame and the decoder is drained
                    // by passing an empty packet (data = null, size = 0)
                    while (gotFrame != 0 && receiveFrameResult > 0)
                    {
                        outputFrame = new AVSubtitle();

                        var emptyPacket = ffmpeg.av_packet_alloc();
                        emptyPacket->data = null;
                        emptyPacket->size = 0;

                        // Receive the frames in a loop
                        receiveFrameResult = ffmpeg.avcodec_decode_subtitle2(CodecContext, &outputFrame, &gotFrame, emptyPacket);
                        if (gotFrame != 0 && receiveFrameResult > 0)
                        {
                            // Send the subtitle to processing
                            receivedFrameCount += 1;
                            ProcessFrame(&outputFrame);
                        }

                        // free the empty packet
                        ffmpeg.av_packet_free(&emptyPacket);

                        // once the subtitle is processed. Release it from memory
                        ffmpeg.avsubtitle_free(&outputFrame);
                    }
                }
            }

            // The packets are alwasy sent. We dequeue them and keep a reference to them
            // in the SentPackets queue
            SentPackets.Push(Packets.Dequeue());

            // Release the sent packets if 1 or more frames were received in the packet
            if (receivedFrameCount >= 1)
            {
                // We clear the sent packet queue (releasing packet from unmanaged memory also)
                // because we got at least 1 frame from the packet.
                SentPackets.Clear();
            }

            return receivedFrameCount;
        }

        /// <summary>
        /// Processes the audio or video frame.
        /// </summary>
        /// <param name="packet">The packet.</param>
        protected virtual void ProcessFrame(AVFrame* frame) { }

        /// <summary>
        /// Processes the subtitle frame.
        /// </summary>
        /// <param name="packet">The packet.</param>
        protected virtual void ProcessFrame(AVSubtitle* frame) { }

        #endregion

        #region IDisposable Support

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="alsoManaged"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        protected virtual void Dispose(bool alsoManaged)
        {
            if (!IsDisposed)
            {
                if (alsoManaged)
                {
                    if (CodecContext != null)
                    {
                        fixed (AVCodecContext** codecContext = &CodecContext)
                            ffmpeg.avcodec_free_context(codecContext);

                        // free all the pending and sent packets
                        ClearPacketQueues();

                    }

                    CodecContext = null;
                }

                IsDisposed = true;
            }
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        #endregion

    }

}
