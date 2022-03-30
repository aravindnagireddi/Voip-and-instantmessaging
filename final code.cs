using System.Diagnostics;
using System.Windows;
using PhoneVoIPApp.BackEnd;
using PhoneVoIPApp.BackEnd.OutOfProcess;

namespace PhoneVoIPApp.Agents
{
    
        public static class AgentHost
    {
        #region Methods

        /// <summary>
        /// agent starts running.
        /// </summary>
        internal static void OnAgentStarted()
        {
          
            BackEnd.Globals.Instance.StartServer(RegistrationHelper.OutOfProcServerClassNames);
        }

        #endregion

        #region Private members

        /// <summary>
        /// Class constructor
        /// </summary>
        static AgentHost()
        {
            
            Deployment.Current.Dispatcher.BeginInvoke(delegate
            {
                Application.Current.UnhandledException += AgentHost.OnUnhandledException;
            });

            
            AgentHost.videoRenderer = new VideoRenderer();

          
            Globals.Instance.VideoRenderer = AgentHost.videoRenderer;
        }

        
        private static void OnUnhandledException(object sender, ApplicationUnhandledExceptionEventArgs e)
        {
            Debug.WriteLine("[AgentHost] An unhandled exception of type {0} has occurred. Error code: 0x{1:X8}. Message: {2}",
                e.ExceptionObject.GetType(), e.ExceptionObject.HResult, e.ExceptionObject.Message);

            if (Debugger.IsAttached)
            {
                Debugger.Break();
            }
        }

        #endregion

        #region Private
        static VideoRenderer videoRenderer;

        #endregion
    }
}

using System.Diagnostics;
using Microsoft.Phone.Networking.Voip;

namespace PhoneVoIPApp.Agents
{
    /// <summary>
    /// An agent that is launched
    /// </summary>
    public class CallInProgressAgentImpl : VoipCallInProgressAgent
    {
        
        public CallInProgressAgentImpl()
            : base()
        {
        }

        /// <summary>
        /// first call will become active.
        /// </summary>
        protected override void OnFirstCallStarting()
        {
            Debug.WriteLine("[CallInProgressAgentImpl] The first call has started.");

            
            AgentHost.OnAgentStarted();
        }

        /// <summary>
        /// The last call has ended.
        /// </summary>
        protected override void OnCancel()
        {
            Debug.WriteLine("[CallInProgressAgentImpl] The last call has ended. Calling NotifyComplete");

            base.NotifyComplete();
        }
    }
}
namespace PhoneVoIPApp.Agents {
    using System.Xml.Serialization;
    
    
    /// <remarks/>
    [System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "4.0.30319.17613")]
    [System.Diagnostics.DebuggerStepThroughAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType=true, Namespace="WPNotification")]
    [System.Xml.Serialization.XmlRootAttribute(Namespace="WPNotification", IsNullable=false)]
    public partial class Notification {
        
        private string nameField;
        
        private string numberField;
        
        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute(Form=System.Xml.Schema.XmlSchemaForm.Unqualified)]
        public string Name {
            get {
                return this.nameField;
            }
            set {
                this.nameField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute(Form=System.Xml.Schema.XmlSchemaForm.Unqualified)]
        public string Number {
            get {
                return this.numberField;
            }
            set {
                this.numberField = value;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Threading;

namespace PhoneVoIPApp.Agents
{
    public class VideoMediaStreamSource : MediaStreamSource, IDisposable
    {
        public class VideoSample
        {
            public VideoSample(Windows.Storage.Streams.IBuffer _buffer, UInt64 _hnsPresentationTime, UInt64 _hnsSampleDuration)
            {
                buffer = _buffer;
                hnsPresentationTime = _hnsPresentationTime;
                hnsSampleDuration = _hnsSampleDuration;
            }

            public Windows.Storage.Streams.IBuffer buffer;
            public UInt64 hnsPresentationTime;
            public UInt64 hnsSampleDuration;
        }

        private const int maxQueueSize = 4;
        private int _frameWidth;
        private int _frameHeight;
        private bool isDisposed = false;
        private Queue<VideoSample> _sampleQueue;

        private object lockObj = new object();
        private ManualResetEvent shutdownEvent;

        private int _outstandingGetVideoSampleCount;

        private MediaStreamDescription _videoDesc;
        private Dictionary<MediaSampleAttributeKeys, string> _emptySampleDict = new Dictionary<MediaSampleAttributeKeys, string>();

        public VideoMediaStreamSource(Stream audioStream, int frameWidth, int frameHeight)
        {
            _frameWidth = frameWidth;
            _frameHeight = frameHeight;
            shutdownEvent = new ManualResetEvent(false);
            _sampleQueue = new Queue<VideoSample>(VideoMediaStreamSource.maxQueueSize);
            _outstandingGetVideoSampleCount = 0;
            BackEnd.Globals.Instance.TransportController.VideoMessageReceived += TransportController_VideoMessageReceived;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void Shutdown()
        {
            shutdownEvent.Set();
            lock (lockObj)
            {
                if (_outstandingGetVideoSampleCount > 0)
                {
                    
                    MediaStreamSample msSamp = new MediaStreamSample(
                        _videoDesc,
                        null,
                        0,
                        0,
                        0,
                        0,
                        _emptySampleDict);
                    ReportGetSampleCompleted(msSamp);
                    _outstandingGetVideoSampleCount = 0;
                }
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!this.isDisposed)
            {
                if (disposing)
                {
                    BackEnd.Globals.Instance.TransportController.VideoMessageReceived -= TransportController_VideoMessageReceived;
                }
                isDisposed = true;
            }
        }

        void TransportController_VideoMessageReceived(Windows.Storage.Streams.IBuffer ibuffer, UInt64 hnsPresenationTime, UInt64 hnsSampleDuration)
        {
            lock (lockObj)
            {
                if (_sampleQueue.Count >= VideoMediaStreamSource.maxQueueSize)
                {
                    // Dequeue and discard oldest
                    _sampleQueue.Dequeue();
                }

                _sampleQueue.Enqueue(new VideoSample(ibuffer, hnsPresenationTime, hnsSampleDuration));
                SendSamples();
            }

        }

        private void SendSamples()
        {
            while (_sampleQueue.Count() > 0 && _outstandingGetVideoSampleCount > 0)
            {
                if (!(shutdownEvent.WaitOne(0)))
                {
                    VideoSample vs = _sampleQueue.Dequeue();
                    Stream s = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsStream(vs.buffer);

                    // Send out the next sample
                    MediaStreamSample msSamp = new MediaStreamSample(
                        _videoDesc,
                        s,
                        0,
                        s.Length,
                        (long)vs.hnsPresentationTime,
                        (long)vs.hnsSampleDuration,
                        _emptySampleDict);

                    ReportGetSampleCompleted(msSamp);
                    _outstandingGetVideoSampleCount--;
                }
                else
                {
                    
                    return;
                }
            }
        }

        private void PrepareVideo()
        {
            // Stream Description 
            Dictionary<MediaStreamAttributeKeys, string> streamAttributes =
                new Dictionary<MediaStreamAttributeKeys, string>();

           
            streamAttributes[MediaStreamAttributeKeys.VideoFourCC] = "H264";
            streamAttributes[MediaStreamAttributeKeys.Height] = _frameHeight.ToString();
            streamAttributes[MediaStreamAttributeKeys.Width] = _frameWidth.ToString();

            MediaStreamDescription msd =
                new MediaStreamDescription(MediaStreamType.Video, streamAttributes);

            _videoDesc = msd;
        }

        private void PrepareAudio()
        {
        }

        protected override void OpenMediaAsync()
        {
            // Init
            Dictionary<MediaSourceAttributesKeys, string> sourceAttributes =
                new Dictionary<MediaSourceAttributesKeys, string>();
            List<MediaStreamDescription> availableStreams =
                new List<MediaStreamDescription>();

            PrepareVideo();

            availableStreams.Add(_videoDesc);

            sourceAttributes[MediaSourceAttributesKeys.Duration] =
                TimeSpan.FromSeconds(0).Ticks.ToString(CultureInfo.InvariantCulture);

            sourceAttributes[MediaSourceAttributesKeys.CanSeek] = false.ToString();

            ReportOpenMediaCompleted(sourceAttributes, availableStreams);
        }

        protected override void GetSampleAsync(MediaStreamType mediaStreamType)
        {
            if (mediaStreamType == MediaStreamType.Audio)
            {
            }
            else if (mediaStreamType == MediaStreamType.Video)
            {
                lock (lockObj)
                {
                    _outstandingGetVideoSampleCount++;
                    SendSamples();
                }
            }
        }

        protected override void CloseMedia()
        {
        }

        protected override void GetDiagnosticAsync(MediaStreamSourceDiagnosticKind diagnosticKind)
        {
            throw new NotImplementedException();
        }

        protected override void SwitchMediaStreamAsync(MediaStreamDescription mediaStreamDescription)
        {
            throw new NotImplementedException();
        }

        protected override void SeekAsync(long seekToTime)
        {
            ReportSeekCompleted(seekToTime);
        }
    }
}
using Microsoft.Phone.Media;
using PhoneVoIPApp.BackEnd;
using System;
using System.Diagnostics;
using System.Windows;

namespace PhoneVoIPApp.Agents
{
    
    internal class VideoRenderer : IVideoRenderer
    {
        
        internal VideoRenderer()
        {
        }

        #region IVideoRenderer methods

        
        public void Start()
        {
            if (this.isRendering)
                return; 

            Deployment.Current.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    Debug.WriteLine("[VideoRenderer::Start] Video rendering setup");
                    StartMediaStreamer();
                    this.isRendering = true;
                }
                catch (Exception err)
                {
                    Debug.WriteLine("[VideoRenderer::Start] " + err.Message);
                }
            });
        }

        private void StartMediaStreamer()
        {
            if (mediaStreamer == null)
            {
                mediaStreamer = MediaStreamerFactory.CreateMediaStreamer(123);
            }

            
            mediaStreamSource = new VideoMediaStreamSource(null, 640, 480);
            mediaStreamer.SetSource(mediaStreamSource);
        }

        
        public void Stop()
        {
            Deployment.Current.Dispatcher.BeginInvoke(() =>
            {
                if (!this.isRendering)
                    return; 

                Debug.WriteLine("[VoIP Background Process] Video rendering stopped.");
                mediaStreamSource.Shutdown();                
                mediaStreamSource.Dispose();
                mediaStreamSource = null;
                mediaStreamer.Dispose();
                mediaStreamer = null;

                this.isRendering = false;
            });
        }

        #endregion

        #region Private members
        private bool isRendering;
        private VideoMediaStreamSource mediaStreamSource;
        private MediaStreamer mediaStreamer;

        #endregion
    }
}
