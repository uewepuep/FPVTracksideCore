using System;
using System.Threading.Tasks;
using Composition;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Tools;
using UI;
using ZXing;

namespace UI.Video
{
    public class QRScanner : IPreProcessable, IDisposable
    {
        private FrameNodeThumb frameNode;
        private RenderTarget2D renderTarget;
        private Drawer drawer;
        private Texture2D scanTexture;
        private byte[] rawData;
        private bool dataReady;
        private volatile bool scanInProgress;
        private volatile bool scanRequired;
        private volatile string detectedName;
        private object dataLock;
        private DateTime nextScanTime;

        public event Action<string> OnTextDetected;

        private const int ScanWidth = 320;
        private const int ScanHeight = 240;

        public bool Disposed { get; private set; }

        public QRScanner(FrameNodeThumb frameNode)
        {
            this.frameNode = frameNode;
            dataLock = new object();
            nextScanTime = DateTime.MinValue;
            rawData = new byte[ScanWidth * ScanHeight * 4];
            frameNode.Source.OnFrameEvent += OnFrameArrived;
        }

        public void Dispose()
        {
            Disposed = true;    

            frameNode.Source.OnFrameEvent -= OnFrameArrived;

            if (renderTarget != null)
            {
                renderTarget.Dispose();
                renderTarget = null;
            }

            if (drawer != null)
            {
                drawer.Dispose();
                drawer = null;
            }
        }

        private void OnFrameArrived(long sampleTime, long processNumber)
        {
            if (scanRequired)
            {
                frameNode.CompositorLayer?.PreProcess(this, false);
            }
        }

        public void PreProcess(Drawer id)
        {
            frameNode.Source.UpdateTexture(id.GraphicsDevice, id.FrameCount, ref scanTexture);
            if (scanTexture == null)
                return;

            if (renderTarget == null)
            {
                renderTarget = new RenderTarget2D(id.GraphicsDevice, ScanWidth, ScanHeight);
            }

            if (drawer == null)
            {
                drawer = new Drawer(id.GraphicsDevice);
                drawer.CanMultiThread = false;
            }

            try
            {
                drawer.GraphicsDevice.SetRenderTarget(renderTarget);
                drawer.GraphicsDevice.Clear(Color.Black);
                drawer.Begin();
                drawer.Draw(scanTexture, new Rectangle(0, 0, scanTexture.Width, scanTexture.Height), new Rectangle(0, 0, ScanWidth, ScanHeight), Color.White, 1);
                drawer.End();
            }
            catch
            {
                renderTarget?.Dispose();
                renderTarget = null;
                drawer?.Dispose();
                drawer = null;
                return;
            }
            finally
            {
                drawer?.GraphicsDevice.SetRenderTarget(null);
            }

            lock (dataLock)
            {
                renderTarget.GetData<byte>(rawData);
                scanRequired = false;
                dataReady = true;
            }
        }

        public void Update(GameTime gameTime)
        {
            // Disable auto-pause when QR scanning.
            frameNode.Source.DrawnThisGraphicsFrame = true;

            string name = detectedName;
            if (name != null)
            {
                detectedName = null;
                OnTextDetected?.Invoke(name);
            }

            if (DateTime.UtcNow < nextScanTime || scanInProgress)
                return;

            bool hasData;

            lock (dataLock)
            {
                hasData = dataReady;
                dataReady = false;
            }

            if (!hasData)
            {
                scanRequired = true;
                return;
            }

            scanInProgress = true;
            nextScanTime = DateTime.UtcNow.AddSeconds(ApplicationProfileSettings.Instance.QRPilotScanFrequencySeconds);

            Task.Run(() =>
            {
                try
                {
                    BarcodeReaderGeneric reader = new BarcodeReaderGeneric();
                    Result result = reader.Decode(new RGBLuminanceSource(rawData, ScanWidth, ScanHeight, RGBLuminanceSource.BitmapFormat.RGBA32));
                    if (result != null)
                    {
                        detectedName = result.Text;
                        Logger.VideoLog.Log(this, result.Text);
                    }
                }
                catch
                {
                }
                finally
                {
                    scanInProgress = false;
                }
            });
        }
    }
}
