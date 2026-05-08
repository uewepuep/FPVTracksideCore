using Composition;
using Composition.Text;
using Microsoft.Xna.Framework;
using UI;
using RaceLib;
using ImageServer;
using System.Reflection;
using System.Runtime.InteropServices;

namespace FPVTuxsideCore
{
    public class FPVTuxsideCoreGame : UI.BaseGame
    {
        public TuxPlatformTools Platform => (TuxPlatformTools)PlatformTools;

        public FPVTuxsideCoreGame()
            : base(new TuxPlatformTools())
        {
        }

        protected override void Update(GameTime gameTime)
        {
            Platform.Do();
            base.Update(gameTime);
        }

        [DllImport("libSDL2-2.0.so.0")]
        private static extern IntPtr SDL_CreateRGBSurfaceFrom(IntPtr pixels, int width, int height, int depth, int pitch, uint Rmask, uint Gmask, uint Bmask, uint Amask);

        [DllImport("libSDL2-2.0.so.0")]
        private static extern void SDL_FreeSurface(IntPtr surface);

        [DllImport("libSDL2-2.0.so.0")]
        private static extern void SDL_SetWindowIcon(IntPtr window, IntPtr icon);

        protected override void Initialize()
        {
            base.Initialize();
            SetWindowIcon();
        }

        private void SetWindowIcon()
        {
            try
            {
                using (System.IO.Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FPVTuxsideCore.icon.png"))
                {
                    if (stream == null)
                        return;

                    using (SkiaSharp.SKBitmap bitmap = SkiaSharp.SKBitmap.Decode(stream))
                    {
                        SkiaSharp.SKBitmap rgba = bitmap.ColorType == SkiaSharp.SKColorType.Rgba8888
                            ? bitmap
                            : bitmap.Copy(SkiaSharp.SKColorType.Rgba8888);

                        IntPtr pixels = rgba.GetPixels();
                        IntPtr surface = SDL_CreateRGBSurfaceFrom(pixels, rgba.Width, rgba.Height, 32, rgba.RowBytes, 0x000000FF, 0x0000FF00, 0x00FF0000, 0xFF000000);

                        if (surface != IntPtr.Zero)
                        {
                            SDL_SetWindowIcon(Window.Handle, surface);
                            SDL_FreeSurface(surface);
                        }

                        if (rgba != bitmap)
                            rgba.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Tools.Logger.UI.LogException(this, ex);
            }
        }

        protected override void LoadContent()
        {
            FfmpegMediaPlatform.FfmpegGlobalInitializer.Initialize();

            Theme.Initialise(GraphicsDevice, PlatformTools.WorkingDirectory, "Dark");
            DirectoryInfo eventDir = new DirectoryInfo(ApplicationProfileSettings.Instance.EventStorageLocation);
            DatabaseFactory.Init(new DB.DatabaseFactory(Data, eventDir));

            base.LoadContent();
            BitmapFontLibrary.Init(PlatformTools.WorkingDirectory);

            VideoFrameWorks.Available.Add(new FfmpegMediaPlatform.FfmpegMediaFramework());
        }
    }
}
