using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using System;
using System.IO;
using System.Linq;

namespace LensDrop.Services
{
    public class SortResult
    {
        public string LocalSortedPath { get; set; } = string.Empty;
        public string RelativeFolderHierarchy { get; set; } = string.Empty;
    }

    public class MediaSorterService
    {
        public string BuildPreviewPath(
            string baseOutputDir,
            bool separateImgAndVid,
            bool groupByDate,
            bool groupByCameraModel,
            string customEventFolder = "")
        {
            string sub = string.Empty;
            if (!string.IsNullOrWhiteSpace(customEventFolder))
                sub = Path.Combine(sub, SanitizeName(customEventFolder));
            if (separateImgAndVid)
                sub = Path.Combine(sub, "Images");
            if (groupByDate)
                sub = Path.Combine(sub, DateTime.Now.ToString("yyyy-MM-dd"));
            if (groupByCameraModel)
                sub = Path.Combine(sub, "Canon EOS");

            return string.IsNullOrEmpty(sub) ? baseOutputDir : Path.Combine(baseOutputDir, sub);
        }

        public SortResult SortFile(
            string sourceFilePath,
            string baseOutputDir,
            string mediaType,
            bool separateImgAndVid,
            bool groupByDate,
            bool groupByCameraModel,
            string customEventFolder = "",
            bool renameWithDatePrefix = false)
        {
            string relativeSubPath = string.Empty;

            if (!string.IsNullOrWhiteSpace(customEventFolder))
            {
                relativeSubPath = Path.Combine(relativeSubPath, SanitizeName(customEventFolder));
            }

            if (separateImgAndVid)
            {
                string typeFolder = mediaType == "Image" ? "Images" : "Videos";
                relativeSubPath = Path.Combine(relativeSubPath, typeFolder);
            }

            var (cameraModel, dateTaken) = ReadExifMetadata(sourceFilePath);

            if (groupByDate)
            {
                relativeSubPath = Path.Combine(relativeSubPath, dateTaken.ToString("yyyy-MM-dd"));
            }

            if (groupByCameraModel && !string.IsNullOrWhiteSpace(cameraModel))
            {
                relativeSubPath = Path.Combine(relativeSubPath, SanitizeName(cameraModel));
            }

            string targetDir = Path.Combine(baseOutputDir, relativeSubPath);
            System.IO.Directory.CreateDirectory(targetDir);

            string originalName = Path.GetFileName(sourceFilePath);
            string finalName = renameWithDatePrefix
                ? $"{dateTaken:yyyyMMdd_HHmmss}_{originalName}"
                : originalName;

            string destFilePath = Path.Combine(targetDir, finalName);
            File.Move(sourceFilePath, destFilePath, overwrite: true);

            return new SortResult
            {
                LocalSortedPath = destFilePath,
                RelativeFolderHierarchy = relativeSubPath.Replace("\\", "/")
            };
        }

        private (string? CameraModel, DateTime DateTaken) ReadExifMetadata(string filePath)
        {
            try
            {
                var directories = ImageMetadataReader.ReadMetadata(filePath);
                var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
                var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();

                string? model = ifd0?.GetDescription(ExifDirectoryBase.TagModel);
                DateTime date = subIfd?.GetDateTime(ExifDirectoryBase.TagDateTimeOriginal)
                                ?? File.GetCreationTime(filePath);

                return (model, date);
            }
            catch
            {
                return (null, File.GetCreationTime(filePath));
            }
        }

        private string SanitizeName(string input)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                input = input.Replace(c, '_');
            return input.Trim();
        }
    }
}
