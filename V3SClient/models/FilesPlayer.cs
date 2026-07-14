using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Animation;
using Gst;
using Gst.Audio;
using Gst.Video;
using V3SClient.libs;


namespace V3SClient.models
{
    public class FilesPlayer:RtspPlayer
    {

        private List<string> videoFiles { get; set; }
        private int _currFileIdx = 0;
       


        public FilesPlayer(List<string> videoFiles, IntPtr windowHandle, bool is_h264,
            bool isNvidiaGPU=false, int gpuIdSink = 0) 
            : base(videoFiles[0], windowHandle,
                  is_h264, isNvidiaGPU, gpuIdSink)
        {
          this.videoFiles = videoFiles;
            ShowVideoSlider = System.Windows.Visibility.Visible;
            QueryPositionPlaying();
        }
        

        protected  override void ReConnect()
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                _currFileIdx++;
                if (_currFileIdx >= videoFiles.Count)
                {                   
                    this.player.SetState(State.Null);
                    CommandToSupper(PlayerStatus.Stop);
                    return;
                }
                player.SetState(State.Null);
                videoSource["location"] = videoFiles[_currFileIdx];
                player.SetState(State.Playing);
                System.Threading.Tasks.Task.Run(() =>
                {
                    Thread.Sleep(TimeSpan.FromMilliseconds(50));
                    bool ret = this.SetRate(this.currentRate);
                });
            });
        }

        protected override string GetPipelineDescription()
        {

                // mp4
            string h264 ="filesrc name=sourceVideo ! qtdemux name=mux " +
              "mux.video_0 ! queue name=video-queue ! h264parse ! video/x-h264, stream-format=(string)byte-stream, alignment=(string)nal " +
              "! identity name=identity ! h264parse ! video/x-h264, stream-format=(string)avc, alignment=(string)au ! d3d11h264dec ! d3d11convert ! d3d11overlay name=videoOverlay ! d3d11videosink sync=false " +
             "mux.audio_0 ! queue name=audio-queue ! aacparse ! avdec_aac ! audioconvert ! audioresample ! volume name=audioVolume ! wasapisink ";

                

            string h265 = "filesrc name=sourceVideo ! qtdemux name=mux " +
                    "mux.video_0 ! queue name=video-queue ! h265parse ! video/x-h265, stream-format=(string)byte-stream, alignment=(string)nal " +
                    "! identity name=identity ! h265parse ! video/x-h265, stream-format=(string)hvc1, alignment=(string)au ! d3d11h265dec ! d3d11convert ! d3d11overlay name=videoOverlay ! d3d11videosink sync=false " +
                   "mux.audio_0 ! queue name=audio-queue ! aacparse ! avdec_aac ! audioconvert ! audioresample ! volume name=audioVolume ! wasapisink ";
                
            string pipeline_description = this.IsH264 ==true? h264 : h265;
            return pipeline_description;
        }
        protected override bool CreatePipeline()
        {
            try
            {            
               string pipelineDescription = GetPipelineDescription();

                if (player != null)
                    this.Dispose();

                player = (Pipeline)Gst.Parse.Launch(pipelineDescription);
                identity = player.GetByName("identity");
                videoSource = player.GetByName("sourceVideo");
                _currFileIdx = 0;

                if (videoFiles.Count > 0)
                    videoSource["location"] = videoFiles[_currFileIdx];
               
                videoOverlay = player.GetByName("videoOverlay");
                audioVolume = player.GetByName("audioVolume");
                audioVolume["volume"] = 0;
                videoQueue = player.GetByName("video-queue");
                audioQueue = player.GetByName("audio-queue");
                demux = player.GetByName("mux");
                demux.PadAdded += DemuxConnection;

                LoggerManager.GeneralLog(NLog.LogLevel.Error, $"Playback create OK");
                return true;
            }
            catch (Exception e){
                LoggerManager.GeneralLog(NLog.LogLevel.Error, $"Playback error create pp fail:{e.ToString()}");
                return false;
            }
        }

        private void DemuxConnection(object o, PadAddedArgs args)
        {
            Pad newPad = args.NewPad;
            Caps caps = newPad.QueryCaps();

            if (caps == null)
            {
                newPad.Dispose();
                return;
            }
            string capsStr = caps.ToString();

            if (capsStr.Contains("video"))
            {
                Pad videoPad = videoQueue.GetStaticPad("sink");
                newPad.Link(videoPad);
                videoPad.Dispose();
               
            }
            if (capsStr.Contains("audio"))
            {
                Pad audioPad = audioQueue.GetStaticPad("sink");
                newPad.Link(audioPad);
                audioPad.Dispose();
                
            }
            caps.Dispose();
            newPad.Dispose();
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
            demux?.Dispose();
            videoQueue?.Dispose();
            audioQueue?.Dispose();
            audioVolume?.Dispose();
            videoOverlay?.Dispose();
            player?.Dispose();

            windowHandle = IntPtr.Zero;
          
            player?.Dispose();
            player = null;
        }
    }
}















