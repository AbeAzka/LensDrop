using LensDrop.Models;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace LensDrop.Services
{
    public class PhotoEditorService
    {
        public Bitmap RenderPreviewBitmap(string sourceImagePath, PhotoEditSettings settings, int maxDimension = 1200)
        {
            using var original = new Bitmap(sourceImagePath);
            int width = original.Width;
            int height = original.Height;

            if (maxDimension > 0 && (width > maxDimension || height > maxDimension))
            {
                double scale = Math.Min((double)maxDimension / width, (double)maxDimension / height);
                width = Math.Max(1, (int)(width * scale));
                height = Math.Max(1, (int)(height * scale));
            }

            var scaled = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(scaled))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(original, 0, 0, width, height);
            }

            return ApplyPipeline(scaled, settings);
        }

        public void ApplyEditsToFile(string sourceImagePath, string destinationImagePath, PhotoEditSettings settings)
        {
            using var original = new Bitmap(sourceImagePath);
            using var processed = ApplyPipeline(new Bitmap(original), settings);

            string ext = Path.GetExtension(destinationImagePath).ToLowerInvariant();
            Directory.CreateDirectory(Path.GetDirectoryName(destinationImagePath)!);

            if (ext == ".png")
            {
                processed.Save(destinationImagePath, ImageFormat.Png);
            }
            else
            {
                var jpegEncoder = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                using var encoderParams = new EncoderParameters(1);
                encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, (long)Math.Clamp(settings.JpegQuality, 50, 100));
                processed.Save(destinationImagePath, jpegEncoder, encoderParams);
            }
        }

        private Bitmap ApplyPipeline(Bitmap workingBitmap, PhotoEditSettings settings)
        {
            // 1. Rotasi & Flip
            RotateFlipType rf = GetRotateFlipType(settings.RotationDegrees, settings.FlipHorizontal);
            if (rf != RotateFlipType.RotateNoneFlipNone)
            {
                workingBitmap.RotateFlip(rf);
            }

            // 2. ColorMatrix untuk Brightness, Contrast, Saturation
            bool needsColorMatrix =
                Math.Abs(settings.Brightness) > 0.001 ||
                Math.Abs(settings.Contrast - 1.0) > 0.001 ||
                Math.Abs(settings.Saturation - 1.0) > 0.001;

            if (!needsColorMatrix)
                return workingBitmap;

            float b = (float)settings.Brightness;
            float c = (float)settings.Contrast;
            float s = (float)settings.Saturation;

            float t = (1.0f - c) / 2.0f + b;

            // Luminance weights (Rec. 709)
            const float lumR = 0.2126f;
            const float lumG = 0.7152f;
            const float lumB = 0.0722f;

            float sr = (1.0f - s) * lumR;
            float sg = (1.0f - s) * lumG;
            float sb = (1.0f - s) * lumB;

            float[][] matrixElements =
            {
                new[] { c * (sr + s), c * sr,       c * sr,       0f, 0f },
                new[] { c * sg,       c * (sg + s), c * sg,       0f, 0f },
                new[] { c * sb,       c * sb,       c * (sb + s), 0f, 0f },
                new[] { 0f,           0f,           0f,           1f, 0f },
                new[] { t,            t,            t,            0f, 1f }
            };

            var colorMatrix = new ColorMatrix(matrixElements);
            using var attributes = new ImageAttributes();
            attributes.SetColorMatrix(colorMatrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);

            var result = new Bitmap(workingBitmap.Width, workingBitmap.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(result))
            {
                g.DrawImage(
                    workingBitmap,
                    new Rectangle(0, 0, workingBitmap.Width, workingBitmap.Height),
                    0, 0, workingBitmap.Width, workingBitmap.Height,
                    GraphicsUnit.Pixel,
                    attributes
                );
            }

            workingBitmap.Dispose();
            return result;
        }

        private static RotateFlipType GetRotateFlipType(int degrees, bool flipH)
        {
            int norm = ((degrees % 360) + 360) % 360;
            return (norm, flipH) switch
            {
                (0, false) => RotateFlipType.RotateNoneFlipNone,
                (90, false) => RotateFlipType.Rotate90FlipNone,
                (180, false) => RotateFlipType.Rotate180FlipNone,
                (270, false) => RotateFlipType.Rotate270FlipNone,
                (0, true) => RotateFlipType.RotateNoneFlipX,
                (90, true) => RotateFlipType.Rotate90FlipX,
                (180, true) => RotateFlipType.Rotate180FlipX,
                (270, true) => RotateFlipType.Rotate270FlipX,
                _ => RotateFlipType.RotateNoneFlipNone
            };
        }
    }
}
