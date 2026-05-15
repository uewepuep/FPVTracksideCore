using System;
using System.IO;
using System.Threading.Tasks;
using Composition;
using Composition.Nodes;
using Microsoft.Playwright;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Tools;

namespace Browser
{
    public class ChromiumNode : ImageNode, IPreProcessable
    {
        private string url;
        public string Url
        {
            get => url;
            set
            {
                if (url != value)
                {
                    url = value;
                    navigatePending = true;
                }
            }
        }

        public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(5);

        // Null = browser default. Transparent = no background (PNG alpha). Any other color = injected CSS.
        public Color? BackgroundColor { get; set; } = null;

        // Fallback resolution used before the node has been laid out.
        public int PageWidth { get; set; } = 800;
        public int PageHeight { get; set; } = 600;

        private IPlaywright playwright;
        private IBrowser browser;
        private IPage page;

        private byte[] pendingPng;
        private bool hasPendingFrame;
        private readonly object frameLock = new object();

        private DateTime lastCapture = DateTime.MinValue;
        private bool navigatePending;
        private bool browserReady;
        private bool browserStarting;
        private bool capturing;

        public ChromiumNode()
        {
            sharedTexture = false;
        }

        public void Refresh()
        {
            navigatePending = true;
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            if (!string.IsNullOrEmpty(url))
            {
                if (!browserReady && !browserStarting)
                {
                    browserStarting = true;
                    _ = StartBrowserAsync();
                }
                else if (browserReady && !capturing)
                {
                    bool intervalElapsed = DateTime.UtcNow - lastCapture >= RefreshInterval;
                    if (navigatePending || intervalElapsed)
                    {
                        lastCapture = DateTime.UtcNow;
                        capturing = true;
                        _ = CaptureAsync(navigatePending);
                        navigatePending = false;
                    }
                }
            }

            base.Draw(id, parentAlpha);
        }

        private async Task StartBrowserAsync()
        {
            try
            {
                playwright = await Playwright.CreateAsync();
                browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
                page = await browser.NewPageAsync();
                browserReady = true;
                navigatePending = true;
            }
            catch (Exception e)
            {
                Logger.Browser.LogException(this, e);
                browserStarting = false;
            }
        }

        private async Task CaptureAsync(bool navigate)
        {
            try
            {
                if (page == null || Disposed)
                    return;

                int w = BoundsF.Width > 0 ? (int)BoundsF.Width : PageWidth;
                int h = BoundsF.Height > 0 ? (int)BoundsF.Height : PageHeight;

                await page.SetViewportSizeAsync(w, h);

                if (navigate)
                    await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.Load });

                if (BackgroundColor.HasValue)
                {
                    if (BackgroundColor.Value == Color.Transparent)
                    {
                        await page.AddStyleTagAsync(new PageAddStyleTagOptions
                        {
                            Content = "html, body { background: transparent !important; }"
                        });
                    }
                    else
                    {
                        Color c = BackgroundColor.Value;
                        await page.AddStyleTagAsync(new PageAddStyleTagOptions
                        {
                            Content = $"html, body {{ background: rgba({c.R},{c.G},{c.B},{c.A / 255f:.2}) !important; }}"
                        });
                    }
                }

                // PNG over JPEG: lossless and Playwright doesn't support BMP.
                byte[] png = await page.ScreenshotAsync(new PageScreenshotOptions
                {
                    Type = ScreenshotType.Png,
                    OmitBackground = BackgroundColor == Color.Transparent
                });

                lock (frameLock)
                {
                    pendingPng = png;
                    hasPendingFrame = true;
                }

                CompositorLayer?.PreProcess(this, true);
                RequestRedraw();
            }
            catch (Exception e)
            {
                Logger.Browser.LogException(this, e);
            }
            finally
            {
                capturing = false;
            }
        }

        public void PreProcess(Drawer id)
        {
            byte[] png;

            lock (frameLock)
            {
                if (!hasPendingFrame)
                    return;
                png = pendingPng;
                hasPendingFrame = false;
            }

            try
            {
                using MemoryStream ms = new MemoryStream(png);
                Texture2D newTexture = Texture2D.FromStream(id.GraphicsDevice, ms);
                texture?.Dispose();
                texture = newTexture;
                SourceBounds = new Rectangle(0, 0, texture.Width, texture.Height);
                UpdateAspectRatioFromTexture();
                RequestLayout();
            }
            catch (Exception e)
            {
                Logger.Browser.LogException(this, e);
            }
        }

        public override void Dispose()
        {
            _ = CleanupAsync();
            base.Dispose();
        }

        private async Task CleanupAsync()
        {
            try
            {
                if (page != null)
                    await page.CloseAsync();
                if (browser != null)
                    await browser.CloseAsync();
                playwright?.Dispose();
            }
            catch (Exception e)
            {
                Logger.Browser.LogException(this, e);
            }
        }
    }
}
