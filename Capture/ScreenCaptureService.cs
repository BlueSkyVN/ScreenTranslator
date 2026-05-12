using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;

namespace ScreenTranslator.Capture
{
    public class ScreenCaptureService
    {
        /// <summary>
        /// Captures a specific region of the screen and returns a SoftwareBitmap suitable for OCR.
        /// </summary>
        public async Task<SoftwareBitmap?> CaptureRegionAsync(Rectangle region)
        {
            if (region.Width <= 0 || region.Height <= 0)
                return null;

            try
            {
                using (var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb))
                {
                    using (var bg = Graphics.FromImage(bitmap))
                    {
                        bg.CopyFromScreen(region.Left, region.Top, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
                    }

                    using (var ms = new MemoryStream())
                    {
                        bitmap.Save(ms, ImageFormat.Bmp);
                        byte[] bytes = ms.ToArray();

                        var ras = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                        using (var writer = new Windows.Storage.Streams.DataWriter(ras))
                        {
                            writer.WriteBytes(bytes);
                            await writer.StoreAsync();
                            writer.DetachStream();
                        }

                        ras.Seek(0);
                        var decoder = await BitmapDecoder.CreateAsync(ras);
                        var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                            BitmapPixelFormat.Bgra8, 
                            BitmapAlphaMode.Premultiplied);

                        ras.Dispose();
                        return softwareBitmap;
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Capture error: {ex.Message}");
            }
        }
    }
}
