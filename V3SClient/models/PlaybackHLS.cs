using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Gst;
using Gst.App;
using GLib;
using V3SClient.libs;

namespace V3SClient.models
{
    public class PlaybackHLS : RtspPlayer
    {
        private string hlsUrl { get; set; }
        private readonly object _snapshotFrameSync = new object();
        private byte[] _latestSnapshotBgr;
        private int _latestSnapshotWidth;
        private int _latestSnapshotHeight;
        private int _latestSnapshotStride;

        /// <summary>
        /// True when the low-rate appsink branch has received an original decoded
        /// camera frame.  This branch is independent of the size of the WPF tile.
        /// </summary>
        public bool HasDecodedSnapshotFrame
        {
            get
            {
                lock (_snapshotFrameSync)
                    return _latestSnapshotBgr != null && _latestSnapshotWidth > 0 && _latestSnapshotHeight > 0;
            }
        }

        protected override bool ForceAspectRatio
        {
            get { return true; }
        }

        public PlaybackHLS(string hlsUrl, IntPtr windowHandle, bool is_h264,
            bool isNvidiaGPU = false, int gpuIdSink = 0) 
            : base(hlsUrl, windowHandle, is_h264, isNvidiaGPU, gpuIdSink)
        {
            this.hlsUrl = hlsUrl;
            ShowVideoSlider = System.Windows.Visibility.Visible;
            QueryPositionPlaying();
        }

        protected override void ReConnect()
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                if (this.player != null)
                {
                    this.player.SetState(State.Null);
                }
                CommandToSupper(PlayerStatus.Stop);
            });
        }

        protected override string GetPipelineDescription()
        {
            return "";
        }

        public override bool InitPipeline()
        {
            if (_timer != null)
            {
                _timer.Dispose();
                _timer = null;
            }
            bool ret = this.CreatePipeline();
            if (!ret) return false;

            player.Bus.EnableSyncMessageEmission();
            player.Bus.SyncMessage += Monitor;

            if (this.IsH264)
                this.SendSeiNal += ParseSeiNalH264;
            else
                this.SendSeiNal += ParseSeiNalH265;

            this.player.SetState(State.Playing);
            return ret;
        }

        protected override bool CreatePipeline()
        {
            try
            {
                if (player != null)
                    this.Dispose();

                player = new Pipeline("hls-pipeline");

                // 1. Create Base Elements
                videoSource = ElementFactory.Make("souphttpsrc", "videoSource");
                if (!string.IsNullOrEmpty(hlsUrl))
                videoSource["location"] = hlsUrl;
                videoSource["ssl-use-system-ca-file"] = true;
                videoSource["ssl-strict"] = true;

                Element hlsDemux = ElementFactory.Make("hlsdemux", "hlsDemux");
                demux = ElementFactory.Make("parsebin", "tsDemux");

                player.Add(videoSource, hlsDemux, demux);
                videoSource.Link(hlsDemux);

                // 2. Dynamic HLS -> TS Link
                hlsDemux.PadAdded += (sender, args) =>
                {
                    Pad hlsSrcPad = args.NewPad;
                    Pad tsSinkPad = demux.GetStaticPad("sink");
                    if (!tsSinkPad.IsLinked) hlsSrcPad.Link(tsSinkPad);
                    tsSinkPad?.Dispose();
                };

                // 3. Dynamic TS -> Video/Audio Link
                demux.PadAdded += (sender, args) =>
                {
                    Pad newPad = args.NewPad;
                    Caps caps = newPad.QueryCaps();

                    if (caps != null && caps.Size > 0)
                    {
                        string capsName = caps.GetStructure(0).Name;

                        // --- VIDEO BRANCH ---
                        if (capsName.StartsWith("video/x-h264", StringComparison.OrdinalIgnoreCase) ||
                            capsName.StartsWith("video/x-h265", StringComparison.OrdinalIgnoreCase))
                        {
                            bool isH265 = capsName.StartsWith("video/x-h265", StringComparison.OrdinalIgnoreCase);
                            this.IsH264 = !isH265;

                            string parseName = isH265 ? "h265parse" : "h264parse";
                            string decName = isH265 ? "d3d11h265dec" : "d3d11h264dec";

                            videoQueue = ElementFactory.Make("queue", "video-queue");
                            // Removed leaky=1 to prevent dropping HLS chunks
                            
                            Element vParse = ElementFactory.Make(parseName, "video-parse");
                            identity = ElementFactory.Make("identity", "identity");

                            Element decoder = ElementFactory.Make(decName, "video-decoder");
                            decoder["qos"] = false;

                            Element converter = ElementFactory.Make("d3d11convert", "video-converter");
                            videoOverlay = ElementFactory.Make("d3d11overlay", "videoOverlay");

                            Element vSink = ElementFactory.Make("d3d11videosink", "video-sink");
                            vSink["async"] = true;
                            vSink["sync"] = true;
                            vSink["qos"] = true;
                            vSink["force-aspect-ratio"] = true;

                            player.Add(videoQueue, vParse, identity, decoder, converter, videoOverlay, vSink);

                            videoQueue.SyncStateWithParent();
                            vParse.SyncStateWithParent();
                            identity.SyncStateWithParent();
                            decoder.SyncStateWithParent();
                            converter.SyncStateWithParent();
                            videoOverlay.SyncStateWithParent();
                            vSink.SyncStateWithParent();

                            Element.Link(videoQueue, vParse, identity, decoder, converter, videoOverlay, vSink);

                            videoOverlay.Connect("draw", Draw);
                            Pad identity_src = identity.GetStaticPad("src");
                            identity_src.AddProbe(Gst.PadProbeType.Buffer, GetFrameInfo);
                            identity_src.Dispose();

                            Pad vQueueSinkPad = videoQueue.GetStaticPad("sink");
                            newPad.Link(vQueueSinkPad);
                            vQueueSinkPad.Dispose();
                        }
                        // --- AUDIO BRANCH ---
                        else if (capsName.StartsWith("audio/"))
                        {
                            audioQueue = ElementFactory.Make("queue", "audio-queue");
                            // Removed leaky=1 to prevent dropping audio chunks

                            Element aDecodeBin = ElementFactory.Make("decodebin", "audio-decodebin");
                            Element aConvert = ElementFactory.Make("audioconvert", "audio-convert");
                            Element aResample = ElementFactory.Make("audioresample", "audio-resample");

                            audioVolume = ElementFactory.Make("volume", "audioVolume");
                            audioVolume["volume"] = 0;

                            Element aSink = ElementFactory.Make("wasapisink", "audio-sink");
                            aSink["async"] = true;
                            aSink["sync"] = true;

                            player.Add(audioQueue, aDecodeBin, aConvert, aResample, audioVolume, aSink);

                            audioQueue.SyncStateWithParent();
                            aDecodeBin.SyncStateWithParent();
                            aConvert.SyncStateWithParent();
                            aResample.SyncStateWithParent();
                            audioVolume.SyncStateWithParent();
                            aSink.SyncStateWithParent();

                            audioQueue.Link(aDecodeBin);

                            aDecodeBin.PadAdded += (dbSender, dbArgs) =>
                            {
                                Pad dbPad = dbArgs.NewPad;
                                Pad convertSinkPad = aConvert.GetStaticPad("sink");
                                if (!convertSinkPad.IsLinked) dbPad.Link(convertSinkPad);
                                convertSinkPad?.Dispose();
                            };

                            Element.Link(aConvert, aResample, audioVolume, aSink);

                            Pad aQueueSinkPad = audioQueue.GetStaticPad("sink");
                            newPad.Link(aQueueSinkPad);
                            aQueueSinkPad.Dispose();
                        }
                    }
                    caps?.Dispose();
                };

                LoggerManager.GeneralLog(NLog.LogLevel.Info, $" Playback created OK");
                return true;
            }
            catch (Exception e)
            {
                LoggerManager.GeneralLog(NLog.LogLevel.Error, $"Playback error create pipeline fail: {e.ToString()}");
                return false;
            }
        }



        private void SnapshotSink_NewSample(object sender, NewSampleArgs args)
        {
            var sink = sender as AppSink;
            if (sink == null)
                return;

            Sample sample = null;
            try
            {
                sample = sink.PullSample();
                if (sample == null || sample.Buffer == null || sample.Caps == null || sample.Caps.Size == 0)
                    return;

                var structure = sample.Caps.GetStructure(0);
                int width;
                int height;
                if (structure == null || !structure.GetInt("width", out width) || !structure.GetInt("height", out height) ||
                    width <= 0 || height <= 0)
                    return;

                var buffer = sample.Buffer;
                MapInfo map;
                if (!buffer.Map(out map, MapFlags.Read) || map.Data == null || map.Data.Length == 0)
                    return;

                try
                {
                    var stride = map.Data.Length / height;
                    if (stride < width * 3)
                        return;

                    var frame = new byte[map.Data.Length];
                    System.Buffer.BlockCopy(map.Data, 0, frame, 0, frame.Length);
                    lock (_snapshotFrameSync)
                    {
                        _latestSnapshotBgr = frame;
                        _latestSnapshotWidth = width;
                        _latestSnapshotHeight = height;
                        _latestSnapshotStride = stride;
                    }
                }
                finally
                {
                    buffer.Unmap(map);
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogDebug("Không thể nhận frame snapshot playback: " + ex.Message);
            }
            finally
            {
                sample?.Dispose();
            }
        }

        /// <summary>
        /// Saves the latest decoded frame at its source resolution.  The appsink
        /// branch is intentionally throttled to 1 fps, keeping CPU/GPU use low while
        /// still making a current Full HD snapshot available for user actions.
        /// </summary>
        public bool TrySaveDecodedSnapshot(string outputPath)
        {
            byte[] frame;
            int width;
            int height;
            int stride;
            lock (_snapshotFrameSync)
            {
                if (_latestSnapshotBgr == null || _latestSnapshotWidth <= 0 || _latestSnapshotHeight <= 0)
                    return false;

                frame = _latestSnapshotBgr;
                width = _latestSnapshotWidth;
                height = _latestSnapshotHeight;
                stride = _latestSnapshotStride;
            }

            try
            {
                using (var bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format24bppRgb))
                {
                    var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, width, height),
                        System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                    try
                    {
                        var sourceRowBytes = width * 3;
                        for (var row = 0; row < height; row++)
                        {
                            System.Runtime.InteropServices.Marshal.Copy(frame, row * stride,
                                IntPtr.Add(data.Scan0, row * data.Stride), sourceRowBytes);
                        }
                    }
                    finally
                    {
                        bitmap.UnlockBits(data);
                    }

                    bitmap.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
                }
                return true;
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Không thể lưu frame gốc playback");
                return false;
            }
        }

        protected override void ReleasePipeline()
        {
            if (player != null)
            {
                player.SetState(State.Paused);
                player.SetState(State.Ready);
                player.SetState(State.Null);
                player.Bus.DisableSyncMessageEmission();
                var children = player.Children;
                foreach (Element child in children)
                    if (child != null)
                        child.Dispose();
            }
            videoSource?.Dispose();
            videoQueue?.Dispose();
            audioVolume?.Dispose();
            videoOverlay?.Dispose();
            player?.Dispose();

            windowHandle = IntPtr.Zero;
          
            player = null;
        }
    }
}
