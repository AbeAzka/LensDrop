namespace LensDrop.Models
{
    public class PhotoEditSettings
    {
        public int RotationDegrees { get; set; } = 0; // 0, 90, 180, 270
        public bool FlipHorizontal { get; set; } = false;
        public double Brightness { get; set; } = 0.0;   // -0.5 to +0.5
        public double Contrast { get; set; } = 1.0;     // 0.5 to 1.8
        public double Saturation { get; set; } = 1.0;   // 0.0 (B&W) to 1.8
        public int JpegQuality { get; set; } = 95;      // 60 to 100
        public string PresetName { get; set; } = "Original";

        public bool HasEdits =>
            RotationDegrees != 0 ||
            FlipHorizontal ||
            System.Math.Abs(Brightness) > 0.001 ||
            System.Math.Abs(Contrast - 1.0) > 0.001 ||
            System.Math.Abs(Saturation - 1.0) > 0.001;
    }
}
