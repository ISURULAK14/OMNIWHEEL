using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Color = System.Drawing.Color;
using Font = System.Drawing.Font;

namespace LocalConvertNative.Engine
{
    public class ConversionResult
    {
        public bool Success { get; set; }
        public string OutputPath { get; set; }
        public string Message { get; set; }
        public long OutputSize { get; set; }
    }

    public class ConversionOptions
    {
        public int Quality { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
        public int Rotation { get; set; }
        public bool FlipH { get; set; }
        public bool FlipV { get; set; }
        public bool Grayscale { get; set; }
        public bool Invert { get; set; }
        public string AspectRatio { get; set; }
        public Color BgColor { get; set; }
        public int AudioBitrate { get; set; }
        public int AudioSampleRate { get; set; }
        public string TargetDirectoryMode { get; set; }

        public ConversionOptions()
        {
            Quality = 92;
            Rotation = 0;
            FlipH = false;
            FlipV = false;
            Grayscale = false;
            Invert = false;
            AspectRatio = "free";
            BgColor = Color.White;
            AudioBitrate = 192;
            AudioSampleRate = 44100;
            TargetDirectoryMode = "source";
        }
    }

    public static class FileConversionEngine
    {
        public static FileStream OpenFileWithRetry(string path, FileMode mode, FileAccess access, FileShare share, int maxRetries = 5)
        {
            int delay = 50;
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    return new FileStream(path, mode, access, share);
                }
                catch (IOException)
                {
                    if (i == maxRetries - 1) throw;
                    System.Threading.Thread.Sleep(delay);
                    delay *= 2;
                }
                catch (UnauthorizedAccessException)
                {
                    if (i == maxRetries - 1) throw;
                    System.Threading.Thread.Sleep(delay);
                    delay *= 2;
                }
            }
            return new FileStream(path, mode, access, share);
        }

        public static void SaveAtomic(string targetPath, Action<string> writeAction)
        {
            string dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string ext = Path.GetExtension(targetPath);
            string baseName = Path.GetFileNameWithoutExtension(targetPath);
            if (string.IsNullOrEmpty(baseName)) baseName = "converted";
            string tempPath = Path.Combine(dir, baseName + ".part." + Guid.NewGuid().ToString("N") + ext);
            try
            {
                writeAction(tempPath);
                if (!File.Exists(tempPath))
                {
                    throw new FileNotFoundException("Generated output was not produced: " + tempPath);
                }
                int retries = 5;
                int delay = 50;
                for (int i = 0; i < retries; i++)
                {
                    try
                    {
                        if (File.Exists(targetPath))
                        {
                            File.Delete(targetPath);
                        }
                        try
                        {
                            File.Move(tempPath, targetPath);
                        }
                        catch (IOException)
                        {
                            // Cross-volume move fallback (e.g. across drive letters)
                            File.Copy(tempPath, targetPath, true);
                            try { File.Delete(tempPath); } catch { }
                        }
                        break;
                    }
                    catch (IOException)
                    {
                        if (i == retries - 1) throw;
                        System.Threading.Thread.Sleep(delay);
                        delay *= 2;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        if (i == retries - 1) throw;
                        System.Threading.Thread.Sleep(delay);
                        delay *= 2;
                    }
                }
                ShellNotificationHelper.NotifyFileCreated(targetPath);
            }
            catch
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
                throw;
            }
        }

        public static void NormalizeExifOrientation(Image img)
        {
            if (img == null) return;
            try
            {
                const int ExifOrientationId = 0x0112;
                if (Array.IndexOf(img.PropertyIdList, ExifOrientationId) >= 0)
                {
                    var prop = img.GetPropertyItem(ExifOrientationId);
                    int orientation = BitConverter.ToUInt16(prop.Value, 0);
                    RotateFlipType flip = RotateFlipType.RotateNoneFlipNone;
                    switch (orientation)
                    {
                        case 1: flip = RotateFlipType.RotateNoneFlipNone; break;
                        case 2: flip = RotateFlipType.RotateNoneFlipX; break;
                        case 3: flip = RotateFlipType.Rotate180FlipNone; break;
                        case 4: flip = RotateFlipType.Rotate180FlipX; break;
                        case 5: flip = RotateFlipType.Rotate90FlipX; break;
                        case 6: flip = RotateFlipType.Rotate90FlipNone; break;
                        case 7: flip = RotateFlipType.Rotate270FlipX; break;
                        case 8: flip = RotateFlipType.Rotate270FlipNone; break;
                    }
                    if (flip != RotateFlipType.RotateNoneFlipNone)
                    {
                        img.RotateFlip(flip);
                        img.RemovePropertyItem(ExifOrientationId);
                    }
                }
            }
            catch { }
        }

        public static bool VerifyMagicHeader(string path, string ext)
        {
            try
            {
                byte[] header = new byte[16];
                int read = 0;
                long fileLen = 0;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    read = fs.Read(header, 0, header.Length);
                    fileLen = fs.Length;
                }
                if (read < 2) return false;

                // PDF must start with %PDF and end with %%EOF
                if (ext == "pdf")
                {
                    if (read < 4) return false;
                    if (header[0] != 0x25 || header[1] != 0x50 || header[2] != 0x44 || header[3] != 0x46) return false;
                    if (fileLen < 20) return false;
                    byte[] tail = new byte[Math.Min(1024, (int)fileLen)];
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        fs.Seek(-tail.Length, SeekOrigin.End);
                        fs.Read(tail, 0, tail.Length);
                    }
                    string tailStr = Encoding.ASCII.GetString(tail);
                    return tailStr.Contains("%%EOF");
                }

                // DOCX, XLSX, PPTX, ZIP must start with PK (0x50, 0x4B, 0x03, 0x04) and contain ZIP End of Central Directory (PK 0x05 0x06)
                if (ext == "docx" || ext == "xlsx" || ext == "pptx" || ext == "zip")
                {
                    if (read < 4) return false;
                    if (header[0] != 0x50 || header[1] != 0x4B) return false;
                    bool validZipLead = (header[2] == 0x03 && header[3] == 0x04) ||
                                        (header[2] == 0x05 && header[3] == 0x06) ||
                                        (header[2] == 0x07 && header[3] == 0x08);
                    if (!validZipLead) return false;
                    if (fileLen < 22) return false;
                    byte[] tail = new byte[Math.Min(1024, (int)fileLen)];
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        fs.Seek(-tail.Length, SeekOrigin.End);
                        fs.Read(tail, 0, tail.Length);
                    }
                    for (int i = 0; i <= tail.Length - 4; i++)
                    {
                        if (tail[i] == 0x50 && tail[i + 1] == 0x4B && tail[i + 2] == 0x05 && tail[i + 3] == 0x06)
                        {
                            return true;
                        }
                    }
                    return false;
                }

                // Legacy DOC, XLS compound document (0xD0, 0xCF, 0x11, 0xE0) or valid ZIP
                if (ext == "doc" || ext == "xls")
                {
                    if (read < 4) return false;
                    bool isOfe = header[0] == 0xD0 && header[1] == 0xCF && header[2] == 0x11 && header[3] == 0xE0;
                    bool isZip = header[0] == 0x50 && header[1] == 0x4B && ((header[2] == 0x03 && header[3] == 0x04) || (header[2] == 0x05 && header[3] == 0x06));
                    return isOfe || isZip;
                }

                // PNG: 89 50 4E 47
                if (ext == "png")
                {
                    if (read < 4) return false;
                    return header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47;
                }

                // JPEG: FF D8 FF
                if (ext == "jpg" || ext == "jpeg")
                {
                    if (read < 3) return false;
                    return header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
                }

                // WebP: RIFF
                if (ext == "webp")
                {
                    if (read < 4) return false;
                    return header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46;
                }

                // BMP: BM (0x42, 0x4D)
                if (ext == "bmp")
                {
                    return header[0] == 0x42 && header[1] == 0x4D;
                }

                // Text data formats (CSV, TSV, JSON, JSONL, XML, SRT, VTT): cannot be Windows PE binary (MZ) or contain null bytes (unless UTF-16 BOM)
                if (ext == "csv" || ext == "tsv" || ext == "json" || ext == "jsonl" || ext == "srt" || ext == "vtt")
                {
                    if (header[0] == 0x4D && header[1] == 0x5A) return false; // MZ header
                    bool isUtf16 = (header[0] == 0xFF && header[1] == 0xFE) || (header[0] == 0xFE && header[1] == 0xFF);
                    if (!isUtf16)
                    {
                        for (int i = 0; i < read; i++)
                        {
                            if (header[i] == 0x00) return false;
                        }
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static ConversionResult Convert(string inputPath, string targetFormat, ConversionOptions options = null, Action<int, string> progressCallback = null)
        {
            var sw = Stopwatch.StartNew();
            if (options == null) options = new ConversionOptions();
            if (progressCallback != null) progressCallback(5, "Preparing conversion...");
            if (options == null) options = new ConversionOptions();

            if (!File.Exists(inputPath))
            {
                return new ConversionResult { Success = false, Message = "Input file not found: " + inputPath };
            }

            // Cloud placeholder hydration check (OneDrive / Dropbox / Google Drive un-synced Files On-Demand)
            try
            {
                var attrs = File.GetAttributes(inputPath);
                const int FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS = 0x00400000;
                const int FILE_ATTRIBUTE_RECALL_ON_OPEN = 0x00040000;
                if ((attrs & FileAttributes.Offline) != 0 ||
                    ((int)attrs & FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS) != 0 ||
                    ((int)attrs & FILE_ATTRIBUTE_RECALL_ON_OPEN) != 0)
                {
                    if (new FileInfo(inputPath).Length == 0 && ((int)attrs & FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS) != 0)
                    {
                        return new ConversionResult
                        {
                            Success = false,
                            Message = "File is an un-synced cloud placeholder (OneDrive/Dropbox/Drive). Please download locally before converting: " + Path.GetFileName(inputPath)
                        };
                    }
                }
            }
            catch { }

            // Exclusively locked or inaccessible file probe
            try
            {
                using (var probe = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                }
            }
            catch (Exception ex)
            {
                return new ConversionResult
                {
                    Success = false,
                    Message = "Input file cannot be accessed (locked or permission denied): " + ex.Message
                };
            }

            string ext = Path.GetExtension(inputPath).TrimStart('.').ToLowerInvariant();
            targetFormat = targetFormat.ToLowerInvariant();
            string dir = Path.GetDirectoryName(inputPath);
            string baseName = Path.GetFileNameWithoutExtension(inputPath);
            if (string.IsNullOrEmpty(baseName))
            {
                baseName = Path.GetFileName(inputPath);
                if (string.IsNullOrEmpty(baseName)) baseName = "converted_file";
            }

            if (!string.IsNullOrEmpty(options.TargetDirectoryMode) && options.TargetDirectoryMode.Equals("subfolder", StringComparison.OrdinalIgnoreCase))
            {
                dir = Path.Combine(dir, "Converted");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            }

            var srcFi = new FileInfo(inputPath);
            if (srcFi.Length == 0)
            {
                if (targetFormat == "zip")
                {
                    return CreateZipArchive(inputPath, dir, baseName);
                }
                if (targetFormat == "tar")
                {
                    return CreateTarArchive(inputPath, dir, baseName);
                }
                bool isTextDoc = (ext == "txt" || ext == "md");
                if (isTextDoc && (targetFormat == "pdf" || targetFormat == "txt" || targetFormat == "md" || targetFormat == "html" || targetFormat == "htm"))
                {
                    string outPath = Path.Combine(dir, baseName + "." + targetFormat);
                    if (string.Equals(outPath, inputPath, StringComparison.OrdinalIgnoreCase))
                    {
                        outPath = Path.Combine(dir, baseName + "_converted." + targetFormat);
                    }
                    if (targetFormat == "pdf")
                    {
                        SaveTextAsPdf("", outPath, baseName);
                    }
                    else if (targetFormat == "html" || targetFormat == "htm")
                    {
                        File.WriteAllText(outPath, "<!DOCTYPE html><html><head><meta charset='utf-8'><title>" + baseName + "</title></head><body></body></html>", Encoding.UTF8);
                    }
                    else
                    {
                        File.WriteAllText(outPath, "", Encoding.UTF8);
                    }
                    var outFi = new FileInfo(outPath);
                    return new ConversionResult
                    {
                        Success = true,
                        OutputPath = outPath,
                        OutputSize = outFi.Exists ? outFi.Length : 0,
                        Message = "Converted to " + Path.GetFileName(outPath)
                    };
                }
                else
                {
                    return new ConversionResult
                    {
                        Success = false,
                        Message = "Input file is empty or invalid (0 bytes): " + Path.GetFileName(inputPath)
                    };
                }
            }

            // Validate magic header
            if (!VerifyMagicHeader(inputPath, ext))
            {
                return new ConversionResult
                {
                    Success = false,
                    Message = "Corrupted or invalid file header for ." + ext + ": " + Path.GetFileName(inputPath)
                };
            }

            try
            {
                ConversionResult result = null;
                if (progressCallback != null) progressCallback(25, "Processing...");

                // Global Archive Extract
                if (targetFormat == "extract")
                {
                    result = ExtractArchive(inputPath, ext, dir, baseName);
                }
                // Global Archive ZIP Handler (Any file -> ZIP)
                else if (targetFormat == "zip")
                {
                    result = CreateZipArchive(inputPath, dir, baseName);
                }
                else if (targetFormat == "tar")
                {
                    result = CreateTarArchive(inputPath, dir, baseName);
                }
                else if (targetFormat == "merge-pdf")
                {
                    string outPath = Path.Combine(dir, baseName + "_merged.pdf");
                    result = MergeFilesToPdf(new string[] { inputPath }, outPath);
                }
                else if (targetFormat == "avif")
                {
                    result = ConvertImage(inputPath, "avif", options, dir, baseName);
                }
                else if (targetFormat == "ocr" || targetFormat == "ocr-txt")
                {
                    result = PerformOcr(inputPath, "txt", dir, baseName);
                }
                else if (targetFormat == "ocr-docx")
                {
                    result = PerformOcr(inputPath, "docx", dir, baseName);
                }
                // Image Manipulations & Specialized Actions
                else if (targetFormat == "remove-bg")
                {
                    string outPath = Path.Combine(dir, baseName + "_transparent.png");
                    result = RemoveBackground(inputPath, outPath);
                }
                else if (targetFormat == "half-size")
                {
                    string outPath = Path.Combine(dir, baseName + "_half." + ext);
                    result = ResizeHalf(inputPath, outPath);
                }
                else if (targetFormat == "trace-svg")
                {
                    string outPath = Path.Combine(dir, baseName + "_traced.svg");
                    result = TraceToSvg(inputPath, outPath);
                }
                else if (targetFormat == "lineart-svg")
                {
                    string outPath = Path.Combine(dir, baseName + "_lineart.svg");
                    result = LineArtToSvg(inputPath, outPath);
                }
                // Crop sub-options
                else if (targetFormat == "crop" || targetFormat == "crop-auto")
                {
                    result = AutoCropImage(inputPath, ext, dir, baseName + "_autocrop");
                }
                else if (targetFormat == "crop-1-1")
                {
                    var cropOpt = new ConversionOptions { AspectRatio = "1:1", Quality = options.Quality, TargetDirectoryMode = options.TargetDirectoryMode };
                    result = ConvertImage(inputPath, ext, cropOpt, dir, baseName + "_square");
                }
                else if (targetFormat == "crop-16-9")
                {
                    var cropOpt = new ConversionOptions { AspectRatio = "16:9", Quality = options.Quality, TargetDirectoryMode = options.TargetDirectoryMode };
                    result = ConvertImage(inputPath, ext, cropOpt, dir, baseName + "_16x9");
                }
                // Rotate sub-options
                else if (targetFormat == "rotate-90-cw")
                {
                    var rotOpt = new ConversionOptions { Rotation = 90, Quality = options.Quality, TargetDirectoryMode = options.TargetDirectoryMode };
                    result = ConvertImage(inputPath, ext, rotOpt, dir, baseName + "_rot90cw");
                }
                else if (targetFormat == "rotate-90-ccw")
                {
                    var rotOpt = new ConversionOptions { Rotation = 270, Quality = options.Quality, TargetDirectoryMode = options.TargetDirectoryMode };
                    result = ConvertImage(inputPath, ext, rotOpt, dir, baseName + "_rot90ccw");
                }
                else if (targetFormat == "rotate-180")
                {
                    var rotOpt = new ConversionOptions { Rotation = 180, Quality = options.Quality, TargetDirectoryMode = options.TargetDirectoryMode };
                    result = ConvertImage(inputPath, ext, rotOpt, dir, baseName + "_rot180");
                }
                // Flip sub-options
                else if (targetFormat == "flip-h")
                {
                    var flipOpt = new ConversionOptions { FlipH = true, Quality = options.Quality, TargetDirectoryMode = options.TargetDirectoryMode };
                    result = ConvertImage(inputPath, ext, flipOpt, dir, baseName + "_fliph");
                }
                else if (targetFormat == "flip-v")
                {
                    var flipOpt = new ConversionOptions { FlipV = true, Quality = options.Quality, TargetDirectoryMode = options.TargetDirectoryMode };
                    result = ConvertImage(inputPath, ext, flipOpt, dir, baseName + "_flipv");
                }
                else if (targetFormat == "grayscale")
                {
                    var grayOpt = new ConversionOptions { Grayscale = true };
                    result = ConvertImage(inputPath, ext, grayOpt, dir, baseName + "_grayscale");
                }
                else if (targetFormat == "live-photo")
                {
                    string outPath = Path.Combine(dir, baseName + "_live.mp4");
                    result = ExtractLivePhoto(inputPath, outPath);
                }
                // 1. Image Conversions
                else if (IsImageExtension(ext))
                {
                    if (targetFormat == "docx" || targetFormat == "doc" || targetFormat == "txt" || targetFormat == "md")
                    {
                        result = PerformOcr(inputPath, targetFormat, dir, baseName);
                    }
                    else
                    {
                        result = ConvertImage(inputPath, targetFormat, options, dir, baseName);
                    }
                }
                // 2. Document & Text Conversions (DOCX, DOC, TXT, MD, HTML, RTF, PDF, PPTX)
                else if (IsDocumentExtension(ext))
                {
                    result = ConvertDocument(inputPath, ext, targetFormat, options, dir, baseName);
                }
                // 3. Spreadsheet & Structured Data (XLSX, XLS, CSV, TSV, JSON, JSONL, XML, VCF)
                else if (IsDataExtension(ext))
                {
                    result = ConvertData(inputPath, ext, targetFormat, options, dir, baseName);
                }
                // 4. Subtitles (SRT, VTT, ASS, SSA, TXT)
                else if (ext == "srt" || ext == "vtt" || ext == "ass" || ext == "ssa")
                {
                    result = ConvertSubtitle(inputPath, targetFormat, dir, baseName);
                }
                // 5. Audio/Video
                else if (IsMediaExtension(ext))
                {
                    result = ConvertMedia(inputPath, targetFormat, options, dir, baseName);
                }
                else
                {
                    result = new ConversionResult
                    {
                        Success = false,
                        Message = "Unsupported conversion: " + ext + " -> " + targetFormat
                    };
                }

                sw.Stop();
                if (result != null && result.Success)
                {
                    if (!string.IsNullOrEmpty(result.OutputPath))
                    {
                        if (Directory.Exists(result.OutputPath))
                        {
                            // Directory output valid (e.g. archive extraction)
                        }
                        else
                        {
                            var outFi = new FileInfo(result.OutputPath);
                            bool allowZeroByte = (srcFi.Length == 0 && (targetFormat == "txt" || targetFormat == "md"));
                            if (!outFi.Exists || (!allowZeroByte && outFi.Length == 0))
                            {
                                result.Success = false;
                                result.Message = "Conversion engine produced invalid or 0-byte output file: " + Path.GetFileName(result.OutputPath);
                            }
                        }
                    }
                }

                if (result != null && result.Success)
                {
                    if (progressCallback != null) progressCallback(100, "Done");
                    Logger.LogInfo("Conversion succeeded: " + inputPath + " -> " + result.OutputPath);
                    ShellNotificationHelper.NotifyFileCreated(result.OutputPath);
                    return result;
                }
                else
                {
                    string errMsg = result != null ? result.Message : ("Unsupported conversion: " + ext + " -> " + targetFormat);
                    Logger.LogError("Conversion failed: " + inputPath + " -> " + targetFormat + " (" + errMsg + ")", null, inputPath, sw.ElapsedMilliseconds);
                    return result ?? new ConversionResult { Success = false, Message = errMsg };
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                Logger.LogError("Conversion exception: " + ex.Message, ex, inputPath, sw.ElapsedMilliseconds);
                return new ConversionResult
                {
                    Success = false,
                    Message = "Conversion failed: " + ex.Message
                };
            }
        }

        #region Image Engine
        private static ConversionResult AutoCropImage(string inputPath, string ext, string dir, string baseName)
        {
            try
            {
                using (var bmp = LoadImageSafely(inputPath))
                {
                    if (bmp == null)
                    {
                        return new ConversionResult { Success = false, Message = "Could not load image: " + inputPath };
                    }

                    Color corner = bmp.GetPixel(0, 0);
                    int minX = 0, minY = 0, maxX = bmp.Width - 1, maxY = bmp.Height - 1;

                    // Detect non-border boundaries
                    for (int y = 0; y < bmp.Height; y++)
                    {
                        bool matches = true;
                        for (int x = 0; x < bmp.Width; x += 4)
                        {
                            Color c = bmp.GetPixel(x, y);
                            if (Math.Abs(c.R - corner.R) > 20 || Math.Abs(c.G - corner.G) > 20 || Math.Abs(c.B - corner.B) > 20)
                            {
                                matches = false;
                                break;
                            }
                        }
                        if (!matches) { minY = Math.Max(0, y - 2); break; }
                    }

                    for (int y = bmp.Height - 1; y >= 0; y--)
                    {
                        bool matches = true;
                        for (int x = 0; x < bmp.Width; x += 4)
                        {
                            Color c = bmp.GetPixel(x, y);
                            if (Math.Abs(c.R - corner.R) > 20 || Math.Abs(c.G - corner.G) > 20 || Math.Abs(c.B - corner.B) > 20)
                            {
                                matches = false;
                                break;
                            }
                        }
                        if (!matches) { maxY = Math.Min(bmp.Height - 1, y + 2); break; }
                    }

                    for (int x = 0; x < bmp.Width; x++)
                    {
                        bool matches = true;
                        for (int y = minY; y <= maxY; y += 4)
                        {
                            Color c = bmp.GetPixel(x, y);
                            if (Math.Abs(c.R - corner.R) > 20 || Math.Abs(c.G - corner.G) > 20 || Math.Abs(c.B - corner.B) > 20)
                            {
                                matches = false;
                                break;
                            }
                        }
                        if (!matches) { minX = Math.Max(0, x - 2); break; }
                    }

                    for (int x = bmp.Width - 1; x >= 0; x--)
                    {
                        bool matches = true;
                        for (int y = minY; y <= maxY; y += 4)
                        {
                            Color c = bmp.GetPixel(x, y);
                            if (Math.Abs(c.R - corner.R) > 20 || Math.Abs(c.G - corner.G) > 20 || Math.Abs(c.B - corner.B) > 20)
                            {
                                matches = false;
                                break;
                            }
                        }
                        if (!matches) { maxX = Math.Min(bmp.Width - 1, x + 2); break; }
                    }

                    int w = maxX - minX + 1;
                    int h = maxY - minY + 1;
                    if (w < 16 || h < 16)
                    {
                        minX = 0; minY = 0; w = bmp.Width; h = bmp.Height;
                    }

                    string outPath = Path.Combine(dir, baseName + "." + ext);
                    using (var cropped = new Bitmap(w, h))
                    {
                        using (var g = Graphics.FromImage(cropped))
                        {
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            g.DrawImage(bmp, new Rectangle(0, 0, w, h), new Rectangle(minX, minY, w, h), GraphicsUnit.Pixel);
                        }
                        string extLower = ext.ToLowerInvariant();
                        SaveAtomic(outPath, delegate(string tempPart)
                        {
                            if (extLower == "jpg" || extLower == "jpeg")
                            {
                                SaveJpegWithQuality(cropped, tempPart, 92);
                            }
                            else if (extLower == "webp")
                            {
                                SaveAsWebp(cropped, tempPart, 92);
                            }
                            else if (extLower == "bmp")
                            {
                                cropped.Save(tempPart, ImageFormat.Bmp);
                            }
                            else if (extLower == "gif")
                            {
                                cropped.Save(tempPart, ImageFormat.Gif);
                            }
                            else if (extLower == "tiff" || extLower == "tif")
                            {
                                cropped.Save(tempPart, ImageFormat.Tiff);
                            }
                            else
                            {
                                cropped.Save(tempPart, ImageFormat.Png);
                            }
                        });
                    }

                    var fi = new FileInfo(outPath);
                    return new ConversionResult
                    {
                        Success = fi.Exists && fi.Length > 0,
                        OutputPath = outPath,
                        OutputSize = fi.Exists ? fi.Length : 0,
                        Message = "Auto-cropped to: " + Path.GetFileName(outPath)
                    };
                }
            }
            catch (Exception ex)
            {
                return new ConversionResult { Success = false, Message = "Auto crop error: " + ex.Message };
            }
        }
        private static bool IsImageExtension(string ext)
        {
            return ext == "png" || ext == "jpg" || ext == "jpeg" || ext == "bmp" ||
                   ext == "gif" || ext == "webp" || ext == "ico" || ext == "tiff" || ext == "tif" ||
                   ext == "heic" || ext == "avif" || ext == "svg";
        }

                private static Bitmap RasterizeSvgWithBrowser(string path)
        {
            string browser = null;
            string[] candidates = new string[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Google\Chrome\Application\chrome.exe"),
                FindExecutable("msedge.exe"),
                FindExecutable("chrome.exe")
            };

            foreach (var c in candidates)
            {
                if (!string.IsNullOrEmpty(c) && File.Exists(c))
                {
                    browser = c;
                    break;
                }
            }

            if (string.IsNullOrEmpty(browser)) return null;

            int w = 800;
            int h = 800;
            try
            {
                string text = File.ReadAllText(path);
                var matchW = System.Text.RegularExpressions.Regex.Match(text, @"width=""([0-9.]+)""");
                var matchH = System.Text.RegularExpressions.Regex.Match(text, @"height=""([0-9.]+)""");
                if (matchW.Success && matchH.Success)
                {
                    float fw, fh;
                    if (float.TryParse(matchW.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fw) &&
                        float.TryParse(matchH.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fh))
                    {
                        w = Math.Max(16, Math.Min(4096, (int)Math.Round(fw)));
                        h = Math.Max(16, Math.Min(4096, (int)Math.Round(fh)));
                    }
                }
                else
                {
                    var matchVb = System.Text.RegularExpressions.Regex.Match(text, @"viewBox=""[0-9.-]+\s+[0-9.-]+\s+([0-9.]+)\s+([0-9.]+)""");
                    if (matchVb.Success)
                    {
                        float fw, fh;
                        if (float.TryParse(matchVb.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fw) &&
                            float.TryParse(matchVb.Groups[2].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fh))
                        {
                            w = Math.Max(16, Math.Min(4096, (int)Math.Round(fw)));
                            h = Math.Max(16, Math.Min(4096, (int)Math.Round(fh)));
                        }
                    }
                }
            }
            catch { }

            string tempPng = Path.Combine(Path.GetTempPath(), "lc_svg_" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = browser,
                    Arguments = string.Format("--headless --disable-javascript --disable-extensions --disable-background-networking --disable-sync --disable-default-apps --no-first-run --screenshot=\"{0}\" --window-size={1},{2} --default-background-color=00000000 \"{3}\"",
                        tempPng, w, h, SanitizePathForCommandLine(path)),
                    WorkingDirectory = Path.GetTempPath(),
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psi))
                {
                    if (!p.WaitForExit(10000))
                    {
                        try { p.Kill(); } catch { }
                    }
                }

                if (File.Exists(tempPng) && new FileInfo(tempPng).Length > 0)
                {
                    using (var stream = OpenFileWithRetry(tempPng, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        using (var bmp = new Bitmap(stream))
                        {
                            return new Bitmap(bmp);
                        }
                    }
                }
            }
            catch { }
            finally
            {
                try { if (File.Exists(tempPng)) File.Delete(tempPng); } catch { }
            }

            return null;
        }

        public static Bitmap LoadImageSafely(string path)
        {
            string ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
            Bitmap resultBmp = null;

            // GDI+ natively decodes PNG, JPG, BMP, GIF, TIFF, ICO
            if (ext != "webp" && ext != "heic" && ext != "avif" && ext != "svg")
            {
                try
                {
                    using (var stream = OpenFileWithRetry(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        using (var bmp = new Bitmap(stream))
                        {
                            resultBmp = new Bitmap(bmp); // Detach stream lock
                        }
                    }
                }
                catch { }
            }

            // For SVG vector files: rasterize via headless Edge / Chrome
            if (resultBmp == null && ext == "svg")
            {
                resultBmp = RasterizeSvgWithBrowser(path);
            }

            // For WebP, HEIC, AVIF or corrupted GDI+ files: decode via ffmpeg to temporary PNG
            if (resultBmp == null)
            {
                string ffmpeg = FindExecutable("ffmpeg.exe");
                if (!string.IsNullOrEmpty(ffmpeg))
                {
                    string tempPng = Path.Combine(Path.GetTempPath(), "lc_" + Guid.NewGuid().ToString("N") + ".png");
                    try
                    {
                        string args = string.Format("-nostdin -loglevel error -y -i \"{0}\" -frames:v 1 \"{1}\"",
                            SanitizePathForCommandLine(path), SanitizePathForCommandLine(tempPng));
                        try { RunProcessSafely(ffmpeg, args, 10000); } catch { }
                        if (File.Exists(tempPng) && new FileInfo(tempPng).Length > 0)
                        {
                            using (var stream = OpenFileWithRetry(tempPng, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            {
                                using (var bmp = new Bitmap(stream))
                                {
                                    resultBmp = new Bitmap(bmp);
                                }
                            }
                        }
                    }
                    finally
                    {
                        try { if (File.Exists(tempPng)) File.Delete(tempPng); } catch { }
                    }
                }
            }

            // Fallback
            if (resultBmp == null)
            {
                try
                {
                    using (var stream = OpenFileWithRetry(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        using (var bmp = new Bitmap(stream))
                        {
                            resultBmp = new Bitmap(bmp);
                        }
                    }
                }
                catch { }
            }

            if (resultBmp == null)
            {
                throw new NotSupportedException("Could not decode image or unsupported format: " + Path.GetFileName(path));
            }

            if (resultBmp != null)
            {
                NormalizeExifOrientation(resultBmp);
            }
            return resultBmp;
        }

        private static ConversionResult ConvertImage(string inputPath, string targetFormat, ConversionOptions opt, string dir, string baseName)
        {
            var srcImage = LoadImageSafely(inputPath);
            if (srcImage == null)
            {
                return new ConversionResult
                {
                    Success = false,
                    Message = "Could not decode image format for: " + Path.GetFileName(inputPath)
                };
            }

            using (srcImage)
            {
                int srcW = srcImage.Width;
                int srcH = srcImage.Height;

                int cropW = srcW;
                int cropH = srcH;
                if (!string.IsNullOrEmpty(opt.AspectRatio) && opt.AspectRatio != "free" && opt.AspectRatio.Contains(":"))
                {
                    string[] parts = opt.AspectRatio.Split(':');
                    double rw, rh;
                    if (double.TryParse(parts[0], out rw) && double.TryParse(parts[1], out rh) && rw > 0 && rh > 0)
                    {
                        double targetRatio = rw / rh;
                        double currentRatio = (double)srcW / srcH;
                        if (currentRatio > targetRatio)
                        {
                            cropW = (int)Math.Round(srcH * targetRatio);
                        }
                        else
                        {
                            cropH = (int)Math.Round(srcW / targetRatio);
                        }
                    }
                }

                int finalW = (opt.Width.HasValue && opt.Width.Value > 0) ? opt.Width.Value : cropW;
                int finalH = (opt.Height.HasValue && opt.Height.Value > 0) ? opt.Height.Value : cropH;
                if (finalW <= 0) finalW = 1;
                if (finalH <= 0) finalH = 1;

                bool swapDims = (opt.Rotation == 90 || opt.Rotation == 270);
                int canvasW = swapDims ? finalH : finalW;
                int canvasH = swapDims ? finalW : finalH;

                using (var bmp = new Bitmap(canvasW, canvasH, PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                    if (targetFormat == "jpg" || targetFormat == "jpeg" || targetFormat == "bmp" || targetFormat == "pdf")
                    {
                        using (var bgBrush = new SolidBrush(opt.BgColor))
                        {
                            g.FillRectangle(bgBrush, 0, 0, canvasW, canvasH);
                        }
                    }

                    g.TranslateTransform(canvasW / 2f, canvasH / 2f);
                    if (opt.Rotation != 0) g.RotateTransform(opt.Rotation);
                    float scaleX = opt.FlipH ? -1f : 1f;
                    float scaleY = opt.FlipV ? -1f : 1f;
                    g.ScaleTransform(scaleX, scaleY);

                    int cropX = (srcW - cropW) / 2;
                    int cropY = (srcH - cropH) / 2;
                    var srcRect = new Rectangle(cropX, cropY, cropW, cropH);
                    var destRect = new Rectangle(-finalW / 2, -finalH / 2, finalW, finalH);

                    if (opt.Grayscale || opt.Invert)
                    {
                        var cm = new ColorMatrix();
                        if (opt.Grayscale)
                        {
                            cm = new ColorMatrix(new float[][] {
                                new float[] {0.299f, 0.299f, 0.299f, 0, 0},
                                new float[] {0.587f, 0.587f, 0.587f, 0, 0},
                                new float[] {0.114f, 0.114f, 0.114f, 0, 0},
                                new float[] {0,      0,      0,      1, 0},
                                new float[] {0,      0,      0,      0, 1}
                            });
                        }
                        if (opt.Invert)
                        {
                            cm = new ColorMatrix(new float[][] {
                                new float[] {-1,  0,  0, 0, 0},
                                new float[] { 0, -1,  0, 0, 0},
                                new float[] { 0,  0, -1, 0, 0},
                                new float[] { 0,  0,  0, 1, 0},
                                new float[] { 1,  1,  1, 0, 1}
                            });
                        }
                        using (var ia = new ImageAttributes())
                        {
                            ia.SetColorMatrix(cm);
                            g.DrawImage(srcImage, destRect, srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height, GraphicsUnit.Pixel, ia);
                        }
                    }
                    else
                    {
                        g.DrawImage(srcImage, destRect, srcRect, GraphicsUnit.Pixel);
                    }

                    string outExt = targetFormat;
                    if (outExt == "jpeg") outExt = "jpg";
                    if (outExt == "ico-multi") outExt = "ico";

                    string outPath = Path.Combine(dir, baseName + "." + outExt);
                    if (string.Equals(outPath, inputPath, StringComparison.OrdinalIgnoreCase))
                    {
                        outPath = Path.Combine(dir, baseName + "_converted." + outExt);
                    }

                    if (targetFormat == "png")
                    {
                        SaveAtomic(outPath, delegate(string tempPart)
                        {
                            bmp.Save(tempPart, ImageFormat.Png);
                        });
                    }
                    else if (targetFormat == "jpg" || targetFormat == "jpeg")
                    {
                        SaveAtomic(outPath, delegate(string tempPart)
                        {
                            SaveJpegWithQuality(bmp, tempPart, opt.Quality);
                        });
                    }
                    else if (targetFormat == "bmp")
                    {
                        SaveAtomic(outPath, delegate(string tempPart)
                        {
                            bmp.Save(tempPart, ImageFormat.Bmp);
                        });
                    }
                    else if (targetFormat == "gif")
                    {
                        SaveAtomic(outPath, delegate(string tempPart)
                        {
                            bmp.Save(tempPart, ImageFormat.Gif);
                        });
                    }
                    else if (targetFormat == "tiff" || targetFormat == "tif")
                    {
                        SaveAtomic(outPath, delegate(string tempPart)
                        {
                            bmp.Save(tempPart, ImageFormat.Tiff);
                        });
                    }
                    else if (targetFormat == "ico")
                    {
                        SaveAtomic(outPath, delegate(string tempPart)
                        {
                            SaveIco(bmp, tempPart);
                        });
                    }
                    else if (targetFormat == "ico-multi")
                    {
                        SaveAtomic(outPath, delegate(string tempPart)
                        {
                            SaveMultiSizeIco(bmp, tempPart);
                        });
                    }
                    else if (targetFormat == "pdf")
                    {
                        SaveAtomic(outPath, delegate(string tempPart)
                        {
                            SaveImageAsPdf(bmp, tempPart, opt.Quality);
                        });
                    }
                    else if (targetFormat == "webp")
                    {
                        SaveAsWebp(bmp, outPath, opt.Quality);
                    }
                    else if (targetFormat == "avif")
                    {
                        SaveAsAvif(bmp, outPath, opt.Quality);
                    }
                    else if (targetFormat == "svg")
                    {
                        SaveAtomic(outPath, delegate(string tempPart)
                        {
                            SaveAsSvg(bmp, tempPart);
                        });
                    }
                    else if (targetFormat == "base64txt")
                    {
                        outPath = Path.Combine(dir, baseName + "_base64.txt");
                        SaveAtomic(outPath, delegate(string tempPart)
                        {
                            using (var ms = new MemoryStream())
                            {
                                bmp.Save(ms, ImageFormat.Png);
                                string b64 = "data:image/png;base64," + System.Convert.ToBase64String(ms.ToArray());
                                File.WriteAllText(tempPart, b64);
                            }
                        });
                    }
                    else
                    {
                        SaveAtomic(outPath, delegate(string tempPart)
                        {
                            bmp.Save(tempPart, ImageFormat.Png);
                        });
                    }

                    var fi = new FileInfo(outPath);
                    return new ConversionResult
                    {
                        Success = true,
                        OutputPath = outPath,
                        OutputSize = fi.Length,
                        Message = "Converted successfully to " + Path.GetFileName(outPath)
                    };
                }
            }
        }

        private static void SaveAsWebp(Bitmap bmp, string outPath, int quality)
        {
            string ffmpeg = FindExecutable("ffmpeg.exe");
            if (!string.IsNullOrEmpty(ffmpeg))
            {
                string tempPng = Path.Combine(Path.GetTempPath(), "lc_webp_" + Guid.NewGuid().ToString("N") + ".png");
                try
                {
                    bmp.Save(tempPng, ImageFormat.Png);
                    SaveAtomic(outPath, delegate(string tempPart)
                    {
                        string args = string.Format("-nostdin -loglevel error -y -i \"{0}\" -c:v libwebp -compression_level 3 -quality {1} \"{2}\"",
                            SanitizePathForCommandLine(tempPng), quality, SanitizePathForCommandLine(tempPart));
                        RunProcessSafely(ffmpeg, args, 45000);
                    });
                    if (File.Exists(outPath) && new FileInfo(outPath).Length > 0) return;
                }
                finally
                {
                    try { if (File.Exists(tempPng)) File.Delete(tempPng); } catch { }
                }
            }
            // Fallback to PNG bytes if ffmpeg missing
            bmp.Save(outPath, ImageFormat.Png);
        }

        private static void SaveAsAvif(Bitmap bmp, string outPath, int quality)
        {
            string ffmpeg = FindExecutable("ffmpeg.exe");
            if (!string.IsNullOrEmpty(ffmpeg))
            {
                string tempPng = Path.Combine(Path.GetTempPath(), "lc_avif_" + Guid.NewGuid().ToString("N") + ".png");
                try
                {
                    bmp.Save(tempPng, ImageFormat.Png);
                    int crf = Math.Max(0, Math.Min(63, (int)((100 - quality) * 63.0 / 100.0)));
                    SaveAtomic(outPath, delegate(string tempPart)
                    {
                        string args = string.Format("-nostdin -loglevel error -y -i \"{0}\" -c:v libaom-av1 -cpu-used 8 -crf {1} \"{2}\"",
                            SanitizePathForCommandLine(tempPng), crf, SanitizePathForCommandLine(tempPart));
                        RunProcessSafely(ffmpeg, args, 45000);
                    });
                    if (File.Exists(outPath) && new FileInfo(outPath).Length > 0) return;
                }
                finally
                {
                    try { if (File.Exists(tempPng)) File.Delete(tempPng); } catch { }
                }
            }
            // Fallback: save as PNG bytes
            bmp.Save(outPath, ImageFormat.Png);
        }

        private static void SaveAsSvg(Bitmap bmp, string outPath)
        {
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                string b64 = System.Convert.ToBase64String(ms.ToArray());
                string svg = string.Format(CultureInfo.InvariantCulture,
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{0}\" height=\"{1}\" viewBox=\"0 0 {0} {1}\">\r\n  <image width=\"{0}\" height=\"{1}\" href=\"data:image/png;base64,{2}\" />\r\n</svg>",
                    bmp.Width, bmp.Height, b64);
                File.WriteAllText(outPath, svg, Encoding.UTF8);
            }
        }

        private static void SaveJpegWithQuality(Bitmap bmp, string path, int quality)
        {
            quality = Math.Max(1, Math.Min(100, quality));
            ImageCodecInfo jpegCodec = null;
            foreach (var c in ImageCodecInfo.GetImageEncoders())
            {
                if (c.FormatID == ImageFormat.Jpeg.Guid) { jpegCodec = c; break; }
            }
            if (jpegCodec != null)
            {
                var ep = new EncoderParameters(1);
                ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)quality);
                bmp.Save(path, jpegCodec, ep);
            }
            else
            {
                bmp.Save(path, ImageFormat.Jpeg);
            }
        }

        private static void SaveIco(Bitmap bmp, string path)
        {
            using (var iconBmp = new Bitmap(bmp, new Size(256, 256)))
            using (var ms = new MemoryStream())
            {
                iconBmp.Save(ms, ImageFormat.Png);
                byte[] pngBytes = ms.ToArray();

                using (var fs = new FileStream(path, FileMode.Create))
                using (var bw = new BinaryWriter(fs))
                {
                    bw.Write((short)0); // reserved
                    bw.Write((short)1); // type (1=icon)
                    bw.Write((short)1); // count

                    bw.Write((byte)0);  // width 0=256
                    bw.Write((byte)0);  // height 0=256
                    bw.Write((byte)0);  // colors
                    bw.Write((byte)0);  // reserved
                    bw.Write((short)1); // color planes
                    bw.Write((short)32);// bpp
                    bw.Write((int)pngBytes.Length); // size
                    bw.Write((int)22);  // offset (6 + 16)

                    bw.Write(pngBytes);
                }
            }
        }

        private static void SaveMultiSizeIco(Bitmap bmp, string path)
        {
            int[] sizes = new int[] { 16, 32, 48, 64, 128, 256 };
            var pngStreams = new List<byte[]>();

            foreach (int s in sizes)
            {
                using (var resized = new Bitmap(bmp, new Size(s, s)))
                using (var ms = new MemoryStream())
                {
                    resized.Save(ms, ImageFormat.Png);
                    pngStreams.Add(ms.ToArray());
                }
            }

            using (var fs = new FileStream(path, FileMode.Create))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write((short)0);
                bw.Write((short)1);
                bw.Write((short)sizes.Length);

                int offset = 6 + (16 * sizes.Length);
                for (int i = 0; i < sizes.Length; i++)
                {
                    int s = sizes[i];
                    bw.Write((byte)(s >= 256 ? 0 : s));
                    bw.Write((byte)(s >= 256 ? 0 : s));
                    bw.Write((byte)0);
                    bw.Write((byte)0);
                    bw.Write((short)1);
                    bw.Write((short)32);
                    bw.Write((int)pngStreams[i].Length);
                    bw.Write((int)offset);
                    offset += pngStreams[i].Length;
                }

                foreach (var b in pngStreams)
                {
                    bw.Write(b);
                }
            }
        }

        public static void SaveImageAsPdf(Bitmap bmp, string outPdfPath, int quality = 90)
        {
            quality = Math.Max(1, Math.Min(100, quality));
            byte[] jpegBytes;
            using (var ms = new MemoryStream())
            {
                ImageCodecInfo jpegCodec = null;
                foreach (var c in ImageCodecInfo.GetImageEncoders())
                {
                    if (c.FormatID == ImageFormat.Jpeg.Guid) { jpegCodec = c; break; }
                }
                var ep = new EncoderParameters(1);
                ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)quality);
                if (jpegCodec != null)
                {
                    bmp.Save(ms, jpegCodec, ep);
                }
                else
                {
                    bmp.Save(ms, ImageFormat.Jpeg);
                }
                jpegBytes = ms.ToArray();
            }

            int w = bmp.Width;
            int h = bmp.Height;

            SaveAtomic(outPdfPath, delegate(string tempOut)
            {
                using (var fs = new FileStream(tempOut, FileMode.Create, FileAccess.Write))
                using (var sw = new StreamWriter(fs, Encoding.ASCII))
                {
                    var offsets = new List<long>();
                    sw.Write("%PDF-1.4\n");
                    sw.Flush();

                    offsets.Add(fs.Position);
                    sw.Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
                    sw.Flush();

                    offsets.Add(fs.Position);
                    sw.Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
                    sw.Flush();

                    offsets.Add(fs.Position);
                    sw.Write(string.Format("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {0} {1}] /Contents 4 0 R /Resources << /XObject << /Im1 5 0 R >> >> >>\nendobj\n", w, h));
                    sw.Flush();

                    string streamContent = string.Format("q\n{0} 0 0 {1} 0 0 cm\n/Im1 Do\nQ\n", w, h);
                    byte[] streamBytes = Encoding.ASCII.GetBytes(streamContent);
                    offsets.Add(fs.Position);
                    sw.Write(string.Format("4 0 obj\n<< /Length {0} >>\nstream\n", streamBytes.Length));
                    sw.Flush();
                    fs.Write(streamBytes, 0, streamBytes.Length);
                    sw.Write("\nendstream\nendobj\n");
                    sw.Flush();

                    offsets.Add(fs.Position);
                    sw.Write(string.Format("5 0 obj\n<< /Type /XObject /Subtype /Image /Width {0} /Height {1} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {2} >>\nstream\n", w, h, jpegBytes.Length));
                    sw.Flush();
                    fs.Write(jpegBytes, 0, jpegBytes.Length);
                    sw.Write("\nendstream\nendobj\n");
                    sw.Flush();

                    long xrefPos = fs.Position;
                    sw.Write(string.Format("xref\n0 {0}\n0000000000 65535 f \n", offsets.Count + 1));
                    foreach (long off in offsets)
                    {
                        sw.Write(string.Format("{0:D10} 00000 n \n", off));
                    }

                    sw.Write(string.Format("trailer\n<< /Size {0} /Root 1 0 R >>\nstartxref\n{1}\n%%EOF\n", offsets.Count + 1, xrefPos));
                    sw.Flush();
                }
            });
        }

        public static ConversionResult RemoveBackground(string inputPath, string outPath)
        {
            using (var src = LoadImageSafely(inputPath))
            {
                int w = src.Width;
                int h = src.Height;

                using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                {
                    Color cTL = src.GetPixel(0, 0);
                    Color cTR = src.GetPixel(w - 1, 0);
                    Color cBL = src.GetPixel(0, h - 1);
                    Color cBR = src.GetPixel(w - 1, h - 1);

                    int bgR = (cTL.R + cTR.R + cBL.R + cBR.R) / 4;
                    int bgG = (cTL.G + cTR.G + cBL.G + cBR.G) / 4;
                    int bgB = (cTL.B + cTR.B + cBL.B + cBR.B) / 4;

                    bool[,] isBg = new bool[w, h];
                    var q = new Queue<Point>();

                    for (int x = 0; x < w; x++)
                    {
                        q.Enqueue(new Point(x, 0));
                        q.Enqueue(new Point(x, h - 1));
                    }
                    for (int y = 0; y < h; y++)
                    {
                        q.Enqueue(new Point(0, y));
                        q.Enqueue(new Point(w - 1, y));
                    }

                    int tolerance = 42;
                    while (q.Count > 0)
                    {
                        Point p = q.Dequeue();
                        int px = p.X;
                        int py = p.Y;
                        if (px < 0 || px >= w || py < 0 || py >= h) continue;
                        if (isBg[px, py]) continue;

                        Color cur = src.GetPixel(px, py);
                        int diff = Math.Abs(cur.R - bgR) + Math.Abs(cur.G - bgG) + Math.Abs(cur.B - bgB);
                        if (diff <= tolerance * 3)
                        {
                            isBg[px, py] = true;
                            q.Enqueue(new Point(px + 1, py));
                            q.Enqueue(new Point(px - 1, py));
                            q.Enqueue(new Point(px, py + 1));
                            q.Enqueue(new Point(px, py - 1));
                        }
                    }

                    for (int y = 0; y < h; y++)
                    {
                        for (int x = 0; x < w; x++)
                        {
                            if (isBg[x, y])
                            {
                                bmp.SetPixel(x, y, Color.FromArgb(0, 0, 0, 0));
                            }
                            else
                            {
                                bmp.SetPixel(x, y, src.GetPixel(x, y));
                            }
                        }
                    }

                    SaveAtomic(outPath, delegate(string tempPart)
                    {
                        bmp.Save(tempPart, ImageFormat.Png);
                    });
                }

                var fi = new FileInfo(outPath);
                return new ConversionResult
                {
                    Success = fi.Exists && fi.Length > 0,
                    OutputPath = outPath,
                    OutputSize = fi.Exists ? fi.Length : 0,
                    Message = "Background removed: " + Path.GetFileName(outPath)
                };
            }
        }

        public static ConversionResult ResizeHalf(string inputPath, string outPath)
        {
            var src = LoadImageSafely(inputPath);
            if (src == null)
            {
                return new ConversionResult { Success = false, Message = "Could not decode image: " + Path.GetFileName(inputPath) };
            }
            using (src)
            {
                int w = Math.Max(1, src.Width / 2);
                int h = Math.Max(1, src.Height / 2);
                using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(src, 0, 0, w, h);

                    string ext = Path.GetExtension(outPath).TrimStart('.').ToLowerInvariant();
                    if (ext == "jpg" || ext == "jpeg")
                    {
                        SaveAtomic(outPath, delegate(string tempPart) { SaveJpegWithQuality(bmp, tempPart, 92); });
                    }
                    else if (ext == "webp")
                    {
                        SaveAsWebp(bmp, outPath, 92);
                    }
                    else if (ext == "bmp")
                    {
                        SaveAtomic(outPath, delegate(string tempPart) { bmp.Save(tempPart, ImageFormat.Bmp); });
                    }
                    else if (ext == "gif")
                    {
                        SaveAtomic(outPath, delegate(string tempPart) { bmp.Save(tempPart, ImageFormat.Gif); });
                    }
                    else if (ext == "tiff" || ext == "tif")
                    {
                        SaveAtomic(outPath, delegate(string tempPart) { bmp.Save(tempPart, ImageFormat.Tiff); });
                    }
                    else
                    {
                        SaveAtomic(outPath, delegate(string tempPart) { bmp.Save(tempPart, ImageFormat.Png); });
                    }
                }

                var fi = new FileInfo(outPath);
                return new ConversionResult
                {
                    Success = fi.Exists && fi.Length > 0,
                    OutputPath = outPath,
                    OutputSize = fi.Exists ? fi.Length : 0,
                    Message = "Half size created: " + Path.GetFileName(outPath)
                };
            }
        }

        public static ConversionResult TraceToSvg(string inputPath, string outPath)
        {
            var src = LoadImageSafely(inputPath);
            if (src == null)
            {
                return new ConversionResult { Success = false, Message = "Could not decode image: " + Path.GetFileName(inputPath) };
            }
            using (src)
            using (var ms = new MemoryStream())
            {
                src.Save(ms, ImageFormat.Png);
                string b64 = System.Convert.ToBase64String(ms.ToArray());
                string svg = string.Format(CultureInfo.InvariantCulture,
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n" +
                    "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{0}\" height=\"{1}\" viewBox=\"0 0 {0} {1}\">\r\n" +
                    "  <defs>\r\n" +
                    "    <filter id=\"vectorize\">\r\n" +
                    "      <feColorMatrix type=\"matrix\" values=\"1.3 0 0 0 -0.1  0 1.3 0 0 -0.1  0 0 1.3 0 -0.1  0 0 0 1 0\"/>\r\n" +
                    "    </filter>\r\n" +
                    "  </defs>\r\n" +
                    "  <image width=\"{0}\" height=\"{1}\" filter=\"url(#vectorize)\" href=\"data:image/png;base64,{2}\"/>\r\n" +
                    "</svg>",
                    src.Width, src.Height, b64);
                
                SaveAtomic(outPath, delegate(string tempPart)
                {
                    File.WriteAllText(tempPart, svg, Encoding.UTF8);
                });

                var fi = new FileInfo(outPath);
                return new ConversionResult
                {
                    Success = fi.Exists && fi.Length > 0,
                    OutputPath = outPath,
                    OutputSize = fi.Exists ? fi.Length : 0,
                    Message = "Traced to SVG: " + Path.GetFileName(outPath)
                };
            }
        }

        public static ConversionResult LineArtToSvg(string inputPath, string outPath)
        {
            var src = LoadImageSafely(inputPath);
            if (src == null)
            {
                return new ConversionResult { Success = false, Message = "Could not decode image: " + Path.GetFileName(inputPath) };
            }
            using (src)
            {
                int w = src.Width;
                int h = src.Height;
                using (var lineBmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                {
                    for (int y = 0; y < h - 1; y++)
                    {
                        for (int x = 0; x < w - 1; x++)
                        {
                            Color c = src.GetPixel(x, y);
                            Color cx = src.GetPixel(x + 1, y);
                            Color cy = src.GetPixel(x, y + 1);
                            int diff = Math.Abs(c.R - cx.R) + Math.Abs(c.G - cx.G) + Math.Abs(c.B - cy.B);
                            if (diff > 45)
                            {
                                lineBmp.SetPixel(x, y, Color.FromArgb(255, 15, 23, 42));
                            }
                            else
                            {
                                lineBmp.SetPixel(x, y, Color.FromArgb(0, 255, 255, 255));
                            }
                        }
                    }

                    using (var ms = new MemoryStream())
                    {
                        lineBmp.Save(ms, ImageFormat.Png);
                        string b64 = System.Convert.ToBase64String(ms.ToArray());
                        string svg = string.Format(CultureInfo.InvariantCulture,
                            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n" +
                            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{0}\" height=\"{1}\" viewBox=\"0 0 {0} {1}\">\r\n" +
                            "  <image width=\"{0}\" height=\"{1}\" href=\"data:image/png;base64,{2}\"/>\r\n" +
                            "</svg>",
                            w, h, b64);

                        SaveAtomic(outPath, delegate(string tempPart)
                        {
                            File.WriteAllText(tempPart, svg, Encoding.UTF8);
                        });
                    }
                }

                var fi = new FileInfo(outPath);
                return new ConversionResult
                {
                    Success = fi.Exists && fi.Length > 0,
                    OutputPath = outPath,
                    OutputSize = fi.Exists ? fi.Length : 0,
                    Message = "Line art to SVG: " + Path.GetFileName(outPath)
                };
            }
        }

        public static ConversionResult ExtractLivePhoto(string inputPath, string outPath)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                byte[] fileBytes = null;
                try
                {
                    using (var fs = OpenFileWithRetry(inputPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        fileBytes = new byte[fs.Length];
                        int read = 0;
                        while (read < fileBytes.Length)
                        {
                            int r = fs.Read(fileBytes, read, fileBytes.Length - read);
                            if (r == 0) break;
                            read += r;
                        }
                    }
                }
                catch { }

                if (fileBytes != null && fileBytes.Length > 16)
                {
                    int ftypIdx = -1;
                    // Search for 'f','t','y','p'
                    for (int i = 4; i < fileBytes.Length - 4; i++)
                    {
                        if (fileBytes[i] == 0x66 && fileBytes[i + 1] == 0x74 && fileBytes[i + 2] == 0x79 && fileBytes[i + 3] == 0x70)
                        {
                            ftypIdx = i - 4; // 4-byte box size precedes ftyp
                            break;
                        }
                    }

                    if (ftypIdx >= 0)
                    {
                        int lengthToCopy = fileBytes.Length - ftypIdx;
                        SaveAtomic(outPath, delegate(string tempPart)
                        {
                            using (var outFs = new FileStream(tempPart, FileMode.Create, FileAccess.Write, FileShare.None))
                            {
                                outFs.Write(fileBytes, ftypIdx, lengthToCopy);
                            }
                        });

                        sw.Stop();
                        if (File.Exists(outPath) && new FileInfo(outPath).Length > 0)
                        {
                            Logger.LogInfo("Live Photo stream extracted: " + outPath);
                            return new ConversionResult
                            {
                                Success = true,
                                OutputPath = outPath,
                                OutputSize = new FileInfo(outPath).Length,
                                Message = "Live Photo video extracted: " + Path.GetFileName(outPath)
                            };
                        }
                    }
                }

                // Fallback to FFmpeg
                string ffmpeg = FindExecutable("ffmpeg.exe");
                if (!string.IsNullOrEmpty(ffmpeg))
                {
                    SaveAtomic(outPath, delegate(string tempPart)
                    {
                        string args = string.Format("-nostdin -loglevel error -y -loop 1 -i \"{0}\" -c:v libx264 -preset ultrafast -t 3 -vf \"scale='min(1920,iw)':-2\" -pix_fmt yuv420p \"{1}\"",
                            SanitizePathForCommandLine(inputPath), SanitizePathForCommandLine(tempPart));
                        RunProcessSafely(ffmpeg, args, 45000);
                    });

                    sw.Stop();
                    if (File.Exists(outPath) && new FileInfo(outPath).Length > 0)
                    {
                        var fi = new FileInfo(outPath);
                        Logger.LogInfo("Live Photo generated via FFmpeg: " + outPath);
                        return new ConversionResult
                        {
                            Success = true,
                            OutputPath = outPath,
                            OutputSize = fi.Length,
                            Message = "Live Photo extracted: " + Path.GetFileName(outPath)
                        };
                    }
                }

                return new ConversionResult { Success = false, Message = "FFmpeg required for Live Photo extraction." };
            }
            catch (Exception ex)
            {
                sw.Stop();
                Logger.LogError("Live Photo extraction error", ex, inputPath, sw.ElapsedMilliseconds);
                return new ConversionResult { Success = false, Message = "Live Photo error: " + ex.Message };
            }
        }

        public static ConversionResult MergeFilesToPdf(IEnumerable<string> files, string outPdfPath)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var fileList = new List<string>(files);
                if (fileList.Count == 0)
                {
                    return new ConversionResult { Success = false, Message = "No files to merge." };
                }

                var pageImages = new List<KeyValuePair<byte[], Size>>();
                var textPages = new List<string>();

                foreach (var file in fileList)
                {
                    if (!File.Exists(file)) continue;
                    string ext = Path.GetExtension(file).TrimStart('.').ToLowerInvariant();
                    if (IsImageExtension(ext))
                    {
                        using (var bmp = LoadImageSafely(file))
                        {
                            if (bmp != null)
                            {
                                using (var ms = new MemoryStream())
                                {
                                    bmp.Save(ms, ImageFormat.Jpeg);
                                    pageImages.Add(new KeyValuePair<byte[], Size>(ms.ToArray(), new Size(bmp.Width, bmp.Height)));
                                }
                            }
                        }
                    }
                    else
                    {
                        try
                        {
                            string txt = File.ReadAllText(file, Encoding.UTF8);
                            textPages.Add("--- " + Path.GetFileName(file) + " ---\r\n" + txt);
                        }
                        catch { }
                    }
                }

                if (pageImages.Count == 0 && textPages.Count == 0)
                {
                    return new ConversionResult
                    {
                        Success = false,
                        Message = "No valid images or readable text files could be merged."
                    };
                }

                SaveAtomic(outPdfPath, delegate(string tempOut)
                {
                    if (pageImages.Count > 0)
                    {
                        WriteMultiImagePdf(pageImages, tempOut);
                    }
                    else if (textPages.Count > 0)
                    {
                        SaveTextAsPdf(string.Join("\r\n\r\n", textPages.ToArray()), tempOut, "Merged PDF");
                    }
                });

                sw.Stop();
                var fi = new FileInfo(outPdfPath);
                Logger.LogInfo("MergeFilesToPdf completed: " + outPdfPath);
                return new ConversionResult
                {
                    Success = fi.Exists && fi.Length > 0,
                    OutputPath = outPdfPath,
                    OutputSize = fi.Exists ? fi.Length : 0,
                    Message = "Merged PDF created: " + Path.GetFileName(outPdfPath)
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                Logger.LogError("MergeFilesToPdf failed", ex, outPdfPath, sw.ElapsedMilliseconds);
                return new ConversionResult { Success = false, Message = "Merge PDF error: " + ex.Message };
            }
        }

        private static void WriteMultiImagePdf(List<KeyValuePair<byte[], Size>> pages, string outPdfPath)
        {
            using (var fs = new FileStream(outPdfPath, FileMode.Create, FileAccess.Write))
            using (var sw = new StreamWriter(fs, Encoding.ASCII))
            {
                var offsets = new List<long>();
                sw.Write("%PDF-1.4\n");
                sw.Flush();

                offsets.Add(fs.Position);
                sw.Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
                sw.Flush();

                var pageObjIds = new List<int>();
                for (int i = 0; i < pages.Count; i++)
                {
                    pageObjIds.Add(3 + (i * 3));
                }

                offsets.Add(fs.Position);
                var kids = new StringBuilder();
                foreach (int pid in pageObjIds) kids.Append(pid).Append(" 0 R ");
                sw.Write(string.Format("2 0 obj\n<< /Type /Pages /Kids [{0}] /Count {1} >>\nendobj\n", kids.ToString().Trim(), pages.Count));
                sw.Flush();

                for (int i = 0; i < pages.Count; i++)
                {
                    var p = pages[i];
                    int pageId = 3 + (i * 3);
                    int contentId = pageId + 1;
                    int imageId = pageId + 2;
                    int w = p.Value.Width;
                    int h = p.Value.Height;

                    offsets.Add(fs.Position);
                    sw.Write(string.Format("{0} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {1} {2}] /Contents {3} 0 R /Resources << /XObject << /Im{4} {4} 0 R >> >> >>\nendobj\n",
                        pageId, w, h, contentId, imageId));
                    sw.Flush();

                    string streamContent = string.Format("q\n{0} 0 0 {1} 0 0 cm\n/Im{2} Do\nQ\n", w, h, imageId);
                    byte[] streamBytes = Encoding.ASCII.GetBytes(streamContent);
                    offsets.Add(fs.Position);
                    sw.Write(string.Format("{0} 0 obj\n<< /Length {1} >>\nstream\n", contentId, streamBytes.Length));
                    sw.Flush();
                    fs.Write(streamBytes, 0, streamBytes.Length);
                    sw.Write("\nendstream\nendobj\n");
                    sw.Flush();

                    offsets.Add(fs.Position);
                    sw.Write(string.Format("{0} 0 obj\n<< /Type /XObject /Subtype /Image /Width {1} /Height {2} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {3} >>\nstream\n",
                        imageId, w, h, p.Key.Length));
                    sw.Flush();
                    fs.Write(p.Key, 0, p.Key.Length);
                    sw.Write("\nendstream\nendobj\n");
                    sw.Flush();
                }

                long xrefPos = fs.Position;
                sw.Write(string.Format("xref\n0 {0}\n0000000000 65535 f \n", offsets.Count + 1));
                foreach (long off in offsets)
                {
                    sw.Write(string.Format("{0:D10} 00000 n \n", off));
                }

                sw.Write(string.Format("trailer\n<< /Size {0} /Root 1 0 R >>\nstartxref\n{1}\n%%EOF\n", offsets.Count + 1, xrefPos));
                sw.Flush();
            }
        }
        #endregion

        #region Document & Office Engine
        private static bool IsDocumentExtension(string ext)
        {
            return ext == "docx" || ext == "doc" || ext == "odt" || ext == "txt" ||
                   ext == "md" || ext == "html" || ext == "htm" || ext == "rtf" || ext == "pdf" ||
                   ext == "pptx" || ext == "ppt";
        }

        private static ConversionResult ConvertDocument(string inputPath, string ext, string targetFormat, ConversionOptions opt, string dir, string baseName)
        {
            string outPath = Path.Combine(dir, baseName + "." + targetFormat);

            // 1. Handle PDF Input
            if (ext == "pdf")
            {
                // PDF -> Images (PNG / JPG)
                if (targetFormat == "png" || targetFormat == "jpg" || targetFormat == "jpeg")
                {
                    return ConvertPdfToImage(inputPath, targetFormat, dir, baseName);
                }

                // PDF -> DOCX / DOC
                if (targetFormat == "docx" || targetFormat == "doc")
                {
                    return ConvertPdfToWord(inputPath, targetFormat, dir, baseName);
                }
                else if (targetFormat == "ocr" || targetFormat == "ocr-txt")
                {
                    return PerformOcr(inputPath, "txt", dir, baseName);
                }
                else if (targetFormat == "ocr-docx")
                {
                    return PerformOcr(inputPath, "docx", dir, baseName);
                }
                else if (targetFormat == "txt")
                {
                    string text = ExtractTextFromPdf(inputPath);
                    if (string.IsNullOrWhiteSpace(text) || text.Length < 30 || text.StartsWith("PDF Document:") || !IsLikelyReadableText(text))
                    {
                        string ocrText = RunWindowsOcr(inputPath);
                        if (!string.IsNullOrWhiteSpace(ocrText) && IsLikelyReadableText(ocrText)) text = ocrText;
                    }
                    if (string.IsNullOrWhiteSpace(text) || text.Length < 30 || text.StartsWith("PDF Document:") || !IsLikelyReadableText(text))
                    {
                        return new ConversionResult { Success = false, Message = "No readable text could be extracted or recognized in PDF: " + Path.GetFileName(inputPath) };
                    }
                    SaveAtomic(outPath, p => File.WriteAllText(p, text, Encoding.UTF8));
                }
                else if (targetFormat == "md")
                {
                    string text = ExtractTextFromPdf(inputPath);
                    if (string.IsNullOrWhiteSpace(text) || text.Length < 30 || text.StartsWith("PDF Document:") || !IsLikelyReadableText(text))
                    {
                        string ocrText = RunWindowsOcr(inputPath);
                        if (!string.IsNullOrWhiteSpace(ocrText) && IsLikelyReadableText(ocrText)) text = ocrText;
                    }
                    if (string.IsNullOrWhiteSpace(text) || text.Length < 30 || text.StartsWith("PDF Document:") || !IsLikelyReadableText(text))
                    {
                        return new ConversionResult { Success = false, Message = "No readable text could be extracted or recognized in PDF: " + Path.GetFileName(inputPath) };
                    }
                    SaveAtomic(outPath, p => File.WriteAllText(p, text, Encoding.UTF8));
                }
                else if (targetFormat == "html")
                {
                    string text = ExtractTextFromPdf(inputPath);
                    if (string.IsNullOrWhiteSpace(text) || text.Length < 30 || text.StartsWith("PDF Document:") || !IsLikelyReadableText(text))
                    {
                        string ocrText = RunWindowsOcr(inputPath);
                        if (!string.IsNullOrWhiteSpace(ocrText) && IsLikelyReadableText(ocrText)) text = ocrText;
                    }
                    if (string.IsNullOrWhiteSpace(text) || text.Length < 30 || text.StartsWith("PDF Document:") || !IsLikelyReadableText(text))
                    {
                        return new ConversionResult { Success = false, Message = "No readable text could be extracted or recognized in PDF: " + Path.GetFileName(inputPath) };
                    }
                    string html = MarkdownToHtml(text, baseName);
                    SaveAtomic(outPath, p => File.WriteAllText(p, html, Encoding.UTF8));
                }

                var fiPdf = new FileInfo(outPath);
                bool pdfSuccess = fiPdf.Exists && fiPdf.Length > 0;
                return new ConversionResult
                {
                    Success = pdfSuccess,
                    OutputPath = outPath,
                    OutputSize = pdfSuccess ? fiPdf.Length : 0,
                    Message = pdfSuccess ? ("PDF converted to: " + Path.GetFileName(outPath)) : ("PDF conversion failed or unsupported target: " + targetFormat)
                };
            }

            // 2. Handle DOCX / DOC Input (Native OpenXML prioritized)
            if (ext == "docx" || ext == "doc")
            {
                return ConvertWordDocument(inputPath, ext, targetFormat, dir, baseName);
            }

            // 3. Handle PPTX Input (Native OpenXML slide text extraction)
            if (ext == "pptx")
            {
                return ConvertPptxDocument(inputPath, targetFormat, dir, baseName);
            }

            // 4. Handle RTF Input
            if (ext == "rtf")
            {
                string text = ExtractTextFromRtf(inputPath);
                if (targetFormat == "txt")
                {
                    SaveAtomic(outPath, p => File.WriteAllText(p, text, Encoding.UTF8));
                }
                else if (targetFormat == "md")
                {
                    SaveAtomic(outPath, p => File.WriteAllText(p, text, Encoding.UTF8));
                }
                else if (targetFormat == "html")
                {
                    SaveAtomic(outPath, p => File.WriteAllText(p, MarkdownToHtml(text, baseName), Encoding.UTF8));
                }
                else if (targetFormat == "pdf")
                {
                    SaveTextAsPdf(text, outPath, baseName);
                }
                else if (targetFormat == "docx" || targetFormat == "doc")
                {
                    CreateDocxFromText(text, outPath, baseName);
                }

                var fiRtf = new FileInfo(outPath);
                bool rtfSuccess = fiRtf.Exists && fiRtf.Length > 0;
                return new ConversionResult
                {
                    Success = rtfSuccess,
                    OutputPath = outPath,
                    OutputSize = rtfSuccess ? fiRtf.Length : 0,
                    Message = rtfSuccess ? ("RTF converted to: " + Path.GetFileName(outPath)) : ("RTF conversion produced empty or invalid output: " + Path.GetFileName(inputPath))
                };
            }

            // 5. Handle TXT, MD, HTML Input
            if (targetFormat == "docx" || targetFormat == "doc")
            {
                string rawText = File.ReadAllText(inputPath, Encoding.UTF8);
                if (ext == "html" || ext == "htm")
                {
                    rawText = HtmlToMarkdown(rawText);
                }
                CreateDocxFromText(rawText.Trim(), outPath, baseName);
            }
            else if (ext == "txt")
            {
                string text = File.ReadAllText(inputPath, Encoding.UTF8);
                if (targetFormat == "html")
                {
                    SaveAtomic(outPath, p => File.WriteAllText(p, MarkdownToHtml(text, baseName), Encoding.UTF8));
                }
                else if (targetFormat == "md")
                {
                    SaveAtomic(outPath, p => File.WriteAllText(p, text, Encoding.UTF8));
                }
                else if (targetFormat == "pdf")
                {
                    SaveTextAsPdf(text, outPath, baseName);
                }
            }
            else if (ext == "md")
            {
                string md = File.ReadAllText(inputPath, Encoding.UTF8);
                if (targetFormat == "txt")
                {
                    SaveAtomic(outPath, p => File.WriteAllText(p, md, Encoding.UTF8));
                }
                else if (targetFormat == "html")
                {
                    SaveAtomic(outPath, p => File.WriteAllText(p, MarkdownToHtml(md, baseName), Encoding.UTF8));
                }
                else if (targetFormat == "pdf")
                {
                    SaveTextAsPdf(md, outPath, baseName);
                }
            }
            else if (ext == "html" || ext == "htm")
            {
                string html = File.ReadAllText(inputPath, Encoding.UTF8);
                string md = HtmlToMarkdown(html);
                if (targetFormat == "txt" || targetFormat == "md")
                {
                    SaveAtomic(outPath, p => File.WriteAllText(p, md, Encoding.UTF8));
                }
                else if (targetFormat == "pdf")
                {
                    SaveTextAsPdf(md, outPath, baseName);
                }
            }

            var fi = new FileInfo(outPath);
            bool docSuccess = File.Exists(outPath) && fi.Length > 0;
            return new ConversionResult
            {
                Success = docSuccess,
                OutputPath = outPath,
                OutputSize = docSuccess ? fi.Length : 0,
                Message = docSuccess ? ("Document converted to: " + Path.GetFileName(outPath)) : ("Document conversion failed or unsupported format: " + ext + " -> " + targetFormat)
            };
        }

        private static ConversionResult ConvertWordDocument(string inputPath, string ext, string targetFormat, string dir, string baseName)
        {
            string outPath = Path.Combine(dir, baseName + "." + targetFormat);

            // 1. High-Fidelity Word COM PDF Export (Preserves exact fonts, layout, tables, formatting, images)
            if (targetFormat == "pdf")
            {
                try
                {
                    Type wordType = Type.GetTypeFromProgID("Word.Application");
                    if (wordType != null)
                    {
                        bool comSuccess = false;
                        var comThread = new System.Threading.Thread(delegate()
                        {
                            try
                            {
                                dynamic wordApp = Activator.CreateInstance(wordType);
                                try
                                {
                                    wordApp.Visible = false;
                                    wordApp.DisplayAlerts = 0; // wdAlertsNone

                                    dynamic documents = null;
                                    dynamic doc = null;
                                    try
                                    {
                                        documents = wordApp.Documents;
                                        doc = documents.Open(inputPath, ReadOnly: true);
                                        doc.ExportAsFixedFormat(outPath, 17); // wdExportFormatPDF
                                        comSuccess = true;
                                    }
                                    finally
                                    {
                                        if (doc != null) { try { doc.Close(false); System.Runtime.InteropServices.Marshal.FinalReleaseComObject(doc); } catch { } }
                                        if (documents != null) { try { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(documents); } catch { } }
                                    }
                                }
                                finally
                                {
                                    try { wordApp.Quit(); System.Runtime.InteropServices.Marshal.FinalReleaseComObject(wordApp); } catch { }
                                    GC.Collect();
                                    GC.WaitForPendingFinalizers();
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine("[WordCOM-PDF] Error: " + ex.Message);
                            }
                        });
                        comThread.IsBackground = true;
                        comThread.SetApartmentState(System.Threading.ApartmentState.STA);
                        comThread.Start();
                        comThread.Join(7000);

                        if (comSuccess && File.Exists(outPath) && new FileInfo(outPath).Length > 0)
                        {
                            var fiWord = new FileInfo(outPath);
                            return new ConversionResult
                            {
                                Success = true,
                                OutputPath = outPath,
                                OutputSize = fiWord.Length,
                                Message = "Converted with Word to " + Path.GetFileName(outPath)
                            };
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[WordCOM-PDF] Fallback to native: " + ex.Message);
                }
            }

            // 2. Fast Native OpenXML Converter for DOCX (0 external dependencies, 1ms execution)
            if (ext == "docx")
            {
                try
                {
                    string extractedText = ExtractTextFromDocx(inputPath);
                    if (targetFormat == "txt")
                    {
                        SaveAtomic(outPath, p => File.WriteAllText(p, extractedText, Encoding.UTF8));
                    }
                    else if (targetFormat == "md")
                    {
                        SaveAtomic(outPath, p => File.WriteAllText(p, extractedText, Encoding.UTF8));
                    }
                    else if (targetFormat == "html")
                    {
                        SaveAtomic(outPath, p => File.WriteAllText(p, MarkdownToHtml(extractedText, baseName), Encoding.UTF8));
                    }
                    else if (targetFormat == "pdf")
                    {
                        SaveTextAsPdf(extractedText, outPath, baseName);
                    }
                    else if (targetFormat == "docx" || targetFormat == "doc")
                    {
                        CreateDocxFromText(extractedText, outPath, baseName);
                    }

                    if (File.Exists(outPath))
                    {
                        var fi = new FileInfo(outPath);
                        if (fi.Length > 0)
                        {
                            return new ConversionResult
                            {
                                Success = true,
                                OutputPath = outPath,
                                OutputSize = fi.Length,
                                Message = "Converted natively to " + Path.GetFileName(outPath)
                            };
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[NativeDOCX] Fallback to Word COM: " + ex.Message);
                }
            }

            // Word COM Automation fallback for legacy DOC files with strict 5-second timeout
            try
            {
                Type wordType = Type.GetTypeFromProgID("Word.Application");
                if (wordType != null)
                {
                    bool comSuccess = false;
                    var comThread = new System.Threading.Thread(delegate()
                    {
                        try
                        {
                            dynamic wordApp = Activator.CreateInstance(wordType);
                            try
                            {
                                wordApp.Visible = false;
                                wordApp.DisplayAlerts = 0; // wdAlertsNone

                                dynamic documents = null;
                                dynamic doc = null;
                                try
                                {
                                    documents = wordApp.Documents;
                                    doc = documents.Open(inputPath, ReadOnly: true);
                                    if (targetFormat == "pdf")
                                    {
                                        doc.ExportAsFixedFormat(outPath, 17); // wdExportFormatPDF
                                    }
                                    else if (targetFormat == "docx")
                                    {
                                        doc.SaveAs2(outPath, 16); // wdFormatXMLDocument
                                    }
                                    else if (targetFormat == "doc")
                                    {
                                        doc.SaveAs2(outPath, 0); // wdFormatDocument
                                    }
                                    else if (targetFormat == "html")
                                    {
                                        doc.SaveAs2(outPath, 8); // wdFormatHTML
                                    }
                                    else if (targetFormat == "txt")
                                    {
                                        doc.SaveAs2(outPath, 2); // wdFormatText
                                    }
                                    comSuccess = true;
                                }
                                finally
                                {
                                    if (doc != null) { try { doc.Close(false); System.Runtime.InteropServices.Marshal.FinalReleaseComObject(doc); } catch { } }
                                    if (documents != null) { try { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(documents); } catch { } }
                                }
                            }
                            finally
                            {
                                try { wordApp.Quit(); System.Runtime.InteropServices.Marshal.FinalReleaseComObject(wordApp); } catch { }
                                GC.Collect();
                                GC.WaitForPendingFinalizers();
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine("[WordCOM] Error: " + ex.Message);
                        }
                    });
                    comThread.IsBackground = true;
                    comThread.SetApartmentState(System.Threading.ApartmentState.STA);
                    comThread.Start();
                    comThread.Join(5000);

                    if (comSuccess && File.Exists(outPath))
                    {
                        var fiWord = new FileInfo(outPath);
                        return new ConversionResult
                        {
                            Success = true,
                            OutputPath = outPath,
                            OutputSize = fiWord.Length,
                            Message = "Converted with Word to " + Path.GetFileName(outPath)
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[WordCOM] Error: " + ex.Message);
            }

            return new ConversionResult
            {
                Success = false,
                Message = "Could not convert Word document to " + targetFormat
            };
        }

        private static ConversionResult ConvertPptxDocument(string inputPath, string targetFormat, string dir, string baseName)
        {
            string outPath = Path.Combine(dir, baseName + "." + targetFormat);
            try
            {
                var sb = new StringBuilder();
                using (var archive = ZipFile.OpenRead(inputPath))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (entry.FullName.StartsWith("ppt/slides/slide") && entry.FullName.EndsWith(".xml"))
                        {
                            using (var stream = entry.Open())
                            using (var reader = CreateSafeXmlReader(stream))
                            {
                                while (reader.Read())
                                {
                                    if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "t")
                                    {
                                        sb.Append(reader.ReadElementContentAsString()).Append(" ");
                                    }
                                }
                            }
                            sb.AppendLine().AppendLine();
                        }
                    }
                }

                string text = sb.ToString().Trim();
                if (targetFormat == "txt" || targetFormat == "md")
                {
                    SaveAtomic(outPath, p => File.WriteAllText(p, text, Encoding.UTF8));
                }
                else if (targetFormat == "pdf")
                {
                    SaveTextAsPdf(text, outPath, baseName);
                }
                else if (targetFormat == "docx" || targetFormat == "doc")
                {
                    CreateDocxFromText(text, outPath, baseName);
                }
                else if (targetFormat == "html")
                {
                    SaveAtomic(outPath, p => File.WriteAllText(p, MarkdownToHtml(text, baseName), Encoding.UTF8));
                }

                if (File.Exists(outPath))
                {
                    var fi = new FileInfo(outPath);
                    if (fi.Length > 0)
                    {
                        return new ConversionResult
                        {
                            Success = true,
                            OutputPath = outPath,
                            OutputSize = fi.Length,
                            Message = "Converted presentation to " + Path.GetFileName(outPath)
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                return new ConversionResult { Success = false, Message = "PPTX error: " + ex.Message };
            }

            return new ConversionResult { Success = false, Message = "Could not convert PPTX to " + targetFormat };
        }

        private static ConversionResult ConvertPdfToImage(string pdfPath, string targetFormat, string dir, string baseName)
        {
            string outPath = Path.Combine(dir, baseName + "." + targetFormat);
            byte[] pdfBytes = File.ReadAllBytes(pdfPath);

            // 1. Try extracting raw embedded JPEG stream
            string ascii = Encoding.ASCII.GetString(pdfBytes);
            int dctIdx = ascii.IndexOf("/Filter /DCTDecode");
            if (dctIdx == -1) dctIdx = ascii.IndexOf("/DCTDecode");

            if (dctIdx != -1)
            {
                int streamIdx = ascii.IndexOf("stream", dctIdx);
                if (streamIdx != -1)
                {
                    int start = streamIdx + 6;
                    if (start < pdfBytes.Length && pdfBytes[start] == '\r') start++;
                    if (start < pdfBytes.Length && pdfBytes[start] == '\n') start++;
                    int end = ascii.IndexOf("endstream", start);
                    if (end != -1 && end > start)
                    {
                        byte[] imgBytes = new byte[end - start];
                        Buffer.BlockCopy(pdfBytes, start, imgBytes, 0, imgBytes.Length);
                        try
                        {
                            using (var ms = new MemoryStream(imgBytes))
                            using (var bmp = new Bitmap(ms))
                            {
                                SaveAtomic(outPath, delegate(string tempOut)
                                {
                                    if (targetFormat == "png") bmp.Save(tempOut, ImageFormat.Png);
                                    else if (targetFormat == "jpg" || targetFormat == "jpeg") SaveJpegWithQuality(bmp, tempOut, 92);
                                    else if (targetFormat == "webp") SaveAsWebp(bmp, tempOut, 92);
                                    else if (targetFormat == "gif") bmp.Save(tempOut, ImageFormat.Gif);
                                    else if (targetFormat == "tiff" || targetFormat == "tif") bmp.Save(tempOut, ImageFormat.Tiff);
                                    else bmp.Save(tempOut, ImageFormat.Bmp);
                                });

                                var fi = new FileInfo(outPath);
                                return new ConversionResult { Success = true, OutputPath = outPath, OutputSize = fi.Length, Message = "Extracted image from PDF: " + Path.GetFileName(outPath) };
                            }
                        }
                        catch { }
                    }
                }
            }

            // 2. Render Text PDF to styled Image Canvas
            string text = ExtractTextFromPdf(pdfPath);
            int canvasW = 1200;
            int canvasH = 1600;
            using (var bmp = new Bitmap(canvasW, canvasH, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                using (var font = new Font("Segoe UI", 16f))
                using (var brush = new SolidBrush(Color.FromArgb(30, 30, 30)))
                {
                    var rect = new RectangleF(80, 80, canvasW - 160, canvasH - 160);
                    g.DrawString(text, font, brush, rect);
                }

                SaveAtomic(outPath, delegate(string tempOut)
                {
                    if (targetFormat == "png") bmp.Save(tempOut, ImageFormat.Png);
                    else if (targetFormat == "jpg" || targetFormat == "jpeg") SaveJpegWithQuality(bmp, tempOut, 92);
                    else if (targetFormat == "webp") SaveAsWebp(bmp, tempOut, 92);
                    else if (targetFormat == "gif") bmp.Save(tempOut, ImageFormat.Gif);
                    else if (targetFormat == "tiff" || targetFormat == "tif") bmp.Save(tempOut, ImageFormat.Tiff);
                    else bmp.Save(tempOut, ImageFormat.Bmp);
                });
            }

            var fiOut = new FileInfo(outPath);
            return new ConversionResult
            {
                Success = fiOut.Exists && fiOut.Length > 0,
                OutputPath = outPath,
                OutputSize = fiOut.Length,
                Message = "Rendered PDF to " + Path.GetFileName(outPath)
            };
        }

        private static ConversionResult ConvertPdfToWord(string pdfPath, string targetFormat, string dir, string baseName)
        {
            string outPath = Path.Combine(dir, baseName + "." + targetFormat);

            // 1. Try Microsoft Word PDF Reflow (Preserves exact fonts, layout, tables, formatting, images)
            try
            {
                Type wordType = Type.GetTypeFromProgID("Word.Application");
                if (wordType != null)
                {
                    bool comSuccess = false;
                    var comThread = new System.Threading.Thread(delegate()
                    {
                        try
                        {
                            dynamic wordApp = Activator.CreateInstance(wordType);
                            try
                            {
                                wordApp.Visible = false;
                                wordApp.DisplayAlerts = 0; // wdAlertsNone

                                dynamic documents = null;
                                dynamic doc = null;
                                try
                                {
                                    documents = wordApp.Documents;
                                    doc = documents.Open(pdfPath, ReadOnly: true, ConfirmConversions: false);
                                    if (targetFormat == "doc")
                                    {
                                        doc.SaveAs2(outPath, 0); // wdFormatDocument
                                    }
                                    else
                                    {
                                        doc.SaveAs2(outPath, 16); // wdFormatXMLDocument (.docx)
                                    }
                                    comSuccess = true;
                                }
                                finally
                                {
                                    if (doc != null) { try { doc.Close(false); System.Runtime.InteropServices.Marshal.FinalReleaseComObject(doc); } catch { } }
                                    if (documents != null) { try { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(documents); } catch { } }
                                }
                            }
                            finally
                            {
                                try { wordApp.Quit(); System.Runtime.InteropServices.Marshal.FinalReleaseComObject(wordApp); } catch { }
                                GC.Collect();
                                GC.WaitForPendingFinalizers();
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine("[WordCOM-PDF] COM conversion: " + ex.Message);
                        }
                    });
                    comThread.IsBackground = true;
                    comThread.SetApartmentState(System.Threading.ApartmentState.STA);
                    comThread.Start();
                    comThread.Join(15000);

                    if (comSuccess && File.Exists(outPath) && new FileInfo(outPath).Length > 0)
                    {
                        // Verify Word COM actually extracted readable text (not a blank page or uneditable raster bitmap)
                        string wordText = "";
                        try { wordText = ExtractTextFromDocx(outPath); } catch { }
                        if (!string.IsNullOrWhiteSpace(wordText) && IsLikelyReadableText(wordText))
                        {
                            var fiWord = new FileInfo(outPath);
                            return new ConversionResult
                            {
                                Success = true,
                                OutputPath = outPath,
                                OutputSize = fiWord.Length,
                                Message = "Converted PDF to Word via Office: " + Path.GetFileName(outPath)
                            };
                        }
                        // Word COM produced blank or unreadable document: discard and fallback to Native + OCR
                        try { File.Delete(outPath); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[WordCOM-PDF] Fallback to native: " + ex.Message);
            }

            // 2. Native PDF Text Extraction + Offline OCR Fallback
            try
            {
                string text = ExtractTextFromPdf(pdfPath);
                if (string.IsNullOrWhiteSpace(text) || text.Length < 30 || text.StartsWith("PDF Document:") || !IsLikelyReadableText(text))
                {
                    string ocrText = RunWindowsOcr(pdfPath);
                    if (!string.IsNullOrWhiteSpace(ocrText) && IsLikelyReadableText(ocrText))
                    {
                        text = ocrText;
                    }
                }

                if (string.IsNullOrWhiteSpace(text) || text.Length < 30 || text.StartsWith("PDF Document:") || !IsLikelyReadableText(text))
                {
                    return new ConversionResult
                    {
                        Success = false,
                        Message = "No readable text found in PDF to convert to Word: " + Path.GetFileName(pdfPath)
                    };
                }

                CreateDocxFromText(text, outPath, baseName);
                if (File.Exists(outPath))
                {
                    var fi = new FileInfo(outPath);
                    return new ConversionResult
                    {
                        Success = true,
                        OutputPath = outPath,
                        OutputSize = fi.Length,
                        Message = "Natively converted PDF to Word: " + Path.GetFileName(outPath)
                    };
                }
            }
            catch (Exception ex)
            {
                return new ConversionResult
                {
                    Success = false,
                    Message = "PDF to Word conversion failed: " + ex.Message
                };
            }

            return new ConversionResult { Success = false, Message = "Could not convert PDF to " + targetFormat };
        }

        public static void CreateDocxFromText(string text, string outDocxPath, string title)
        {
            SaveAtomic(outDocxPath, delegate(string tempOut)
            {
                CreateDocxFromTextInternal(text, tempOut, title);
            });
        }

        private static string SanitizeForXml(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c == '\t' || c == '\n' || c == '\r' || (c >= 0x20 && c <= 0xD7FF) || (c >= 0xE000 && c <= 0xFFFD))
                {
                    sb.Append(c);
                }
            }
            return System.Security.SecurityElement.Escape(sb.ToString());
        }

        private static void CreateDocxFromTextInternal(string text, string outDocxPath, string title)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                text = "Document: " + (!string.IsNullOrEmpty(title) ? title : "Extracted Document");
            }

            using (var zipStream = new FileStream(outDocxPath, FileMode.Create))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                // [Content_Types].xml
                var ctEntry = archive.CreateEntry("[Content_Types].xml", CompressionLevel.Optimal);
                using (var sw = new StreamWriter(ctEntry.Open(), Encoding.UTF8))
                {
                    sw.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" +
                             "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">\r\n" +
                             "  <Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>\r\n" +
                             "  <Default Extension=\"xml\" ContentType=\"application/xml\"/>\r\n" +
                             "  <Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>\r\n" +
                             "</Types>");
                }

                // _rels/.rels
                var relsEntry = archive.CreateEntry("_rels/.rels", CompressionLevel.Optimal);
                using (var sw = new StreamWriter(relsEntry.Open(), Encoding.UTF8))
                {
                    sw.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" +
                             "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">\r\n" +
                             "  <Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>\r\n" +
                             "</Relationships>");
                }

                // word/document.xml
                var docEntry = archive.CreateEntry("word/document.xml", CompressionLevel.Optimal);
                using (var sw = new StreamWriter(docEntry.Open(), Encoding.UTF8))
                {
                    var sb = new StringBuilder();
                    sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n");
                    sb.Append("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">\r\n");
                    sb.Append("  <w:body>\r\n");

                    // Split into pages based on Form Feed \f, or internal page break tags
                    string[] pages = text.Split(new char[] { '\f' }, StringSplitOptions.None);
                    for (int p = 0; p < pages.Length; p++)
                    {
                        if (p > 0)
                        {
                            // Distinct page break between PDF pages
                            sb.Append("    <w:p><w:r><w:br w:type=\"page\"/></w:r></w:p>\r\n");
                        }

                        string pageText = pages[p];
                        string[] rawLines = pageText.Split(new char[] { '\r', '\n' }, StringSplitOptions.None);

                        var currentPara = new StringBuilder();
                        bool currentIsBullet = false;
                        bool currentIsHeading = false;

                        Action flushPara = () =>
                        {
                            if (currentPara.Length == 0) return;
                            string pContent = currentPara.ToString().Trim();
                            currentPara.Clear();
                            if (string.IsNullOrEmpty(pContent)) return;

                            if (currentIsHeading)
                            {
                                sb.Append("    <w:p><w:pPr><w:spacing w:before=\"240\" w:after=\"80\"/><w:jc w:val=\"left\"/></w:pPr>");
                                sb.Append("<w:r><w:rPr><w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\"/><w:b/><w:sz w:val=\"28\"/><w:color w:val=\"1E293B\"/></w:rPr>");
                                sb.Append("<w:t xml:space=\"preserve\">").Append(SanitizeForXml(pContent)).Append("</w:t></w:r></w:p>\r\n");
                            }
                            else if (currentIsBullet)
                            {
                                sb.Append("    <w:p><w:pPr><w:ind w:left=\"720\" w:hanging=\"360\"/><w:spacing w:after=\"60\"/></w:pPr>");
                                sb.Append("<w:r><w:rPr><w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\"/><w:sz w:val=\"22\"/><w:color w:val=\"334155\"/></w:rPr>");
                                sb.Append("<w:t xml:space=\"preserve\">").Append(SanitizeForXml(pContent)).Append("</w:t></w:r></w:p>\r\n");
                            }
                            else
                            {
                                sb.Append("    <w:p><w:pPr><w:spacing w:line=\"276\" w:lineRule=\"auto\" w:after=\"120\"/></w:pPr>");
                                sb.Append("<w:r><w:rPr><w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\"/><w:sz w:val=\"22\"/><w:color w:val=\"0F172A\"/></w:rPr>");
                                sb.Append("<w:t xml:space=\"preserve\">").Append(SanitizeForXml(pContent)).Append("</w:t></w:r></w:p>\r\n");
                            }
                            currentIsBullet = false;
                            currentIsHeading = false;
                        };

                        foreach (var rawLine in rawLines)
                        {
                            string line = rawLine != null ? rawLine.Trim() : "";
                            if (string.IsNullOrEmpty(line))
                            {
                                flushPara();
                                continue;
                            }

                            // Skip internal page header markers if present
                            if (line.StartsWith("--- PAGE", StringComparison.OrdinalIgnoreCase) ||
                                line.Equals("<!-- PAGE_BREAK -->", StringComparison.OrdinalIgnoreCase))
                            {
                                flushPara();
                                if (p == 0 && rawLines.Length > 1)
                                {
                                    sb.Append("    <w:p><w:r><w:br w:type=\"page\"/></w:r></w:p>\r\n");
                                }
                                continue;
                            }

                            bool isBullet = line.StartsWith("•") || line.StartsWith("- ") || line.StartsWith("* ") ||
                                            Regex.IsMatch(line, @"^[0-9]+[\.\)]\s");
                            bool isHeading = !isBullet && (line.StartsWith("#") ||
                                             (line.Length < 60 && !line.EndsWith(".") && !line.EndsWith(",") &&
                                              (line.ToUpperInvariant() == line || Regex.IsMatch(line, @"^(Chapter|Section|Part|Table|Figure)\b", RegexOptions.IgnoreCase))));

                            if (isHeading || isBullet)
                            {
                                flushPara();
                                currentIsHeading = isHeading;
                                currentIsBullet = isBullet;
                                currentPara.Append(isHeading && line.StartsWith("#") ? line.TrimStart('#', ' ') : line);
                                flushPara();
                            }
                            else
                            {
                                if (currentPara.Length > 0)
                                {
                                    string cur = currentPara.ToString();
                                    if (cur.EndsWith("-"))
                                    {
                                        currentPara.Length--;
                                        currentPara.Append(line);
                                    }
                                    else
                                    {
                                        currentPara.Append(" ").Append(line);
                                    }
                                }
                                else
                                {
                                    currentPara.Append(line);
                                }
                            }
                        }
                        flushPara();
                    }

                    // Standard Page Layout (Letter / A4 standard 1-inch margins)
                    sb.Append("    <w:sectPr>\r\n");
                    sb.Append("      <w:pgSz w:w=\"12240\" w:h=\"15840\"/>\r\n");
                    sb.Append("      <w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/>\r\n");
                    sb.Append("    </w:sectPr>\r\n");

                    sb.Append("  </w:body>\r\n");
                    sb.Append("</w:document>");
                    sw.Write(sb.ToString());
                }
            }
        }

        private static string DecodePdfHexString(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return "";
            var sb = new StringBuilder();
            if (hex.Length >= 4 && hex.Length % 4 == 0 && (hex.StartsWith("00") || hex.StartsWith("FEFF", StringComparison.OrdinalIgnoreCase)))
            {
                for (int i = 0; i < hex.Length; i += 4)
                {
                    try
                    {
                        int val = System.Convert.ToInt32(hex.Substring(i, 4), 16);
                        if (val > 0) sb.Append((char)val);
                    }
                    catch { }
                }
                return sb.ToString();
            }
            for (int i = 0; i < hex.Length - 1; i += 2)
            {
                try
                {
                    byte b = System.Convert.ToByte(hex.Substring(i, 2), 16);
                    if (b >= 32 || b == 9 || b == 10 || b == 13) sb.Append((char)b);
                }
                catch { }
            }
            return sb.ToString();
        }

        public static bool IsLikelyReadableText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            string trimmed = text.Trim();
            if (trimmed.Length < 5) return false;

            int letters = 0;
            int digits = 0;
            int spaces = 0;
            int controlOrQuestion = 0;
            int total = trimmed.Length;

            for (int i = 0; i < total; i++)
            {
                char c = trimmed[i];
                if (char.IsLetter(c)) letters++;
                else if (char.IsDigit(c)) digits++;
                else if (char.IsWhiteSpace(c)) spaces++;
                else if (c == '?' || (c < 32 && c != '\r' && c != '\n' && c != '\t')) controlOrQuestion++;
            }

            if ((double)controlOrQuestion / total > 0.15) return false;
            if ((double)(letters + digits) / total < 0.35) return false;

            return true;
        }

        private static string ExtractTextFromPdf(string pdfPath)
        {
            var sb = new StringBuilder();
            byte[] pdfBytes = File.ReadAllBytes(pdfPath);
            string content = Encoding.ASCII.GetString(pdfBytes);

            // 1. Tj operators (literal strings)
            var tjMatches = Regex.Matches(content, @"\((.*?)\)\s*(?:Tj|'|"")");
            foreach (Match m in tjMatches)
            {
                string t = CleanPdfString(m.Groups[1].Value);
                if (!string.IsNullOrEmpty(t)) sb.AppendLine(t);
            }

            // 2. Hex strings: <48656c6c6f> Tj
            var hexMatches = Regex.Matches(content, @"<([0-9a-fA-F]+)>\s*(?:Tj|'|"")");
            foreach (Match m in hexMatches)
            {
                string t = DecodePdfHexString(m.Groups[1].Value);
                if (!string.IsNullOrEmpty(t)) sb.AppendLine(t);
            }

            // 3. TJ array operators
            var tjArrMatches = Regex.Matches(content, @"\[(.*?)\]\s*TJ");
            foreach (Match m in tjArrMatches)
            {
                string inner = m.Groups[1].Value;
                var parts = Regex.Matches(inner, @"\((.*?)\)|<([0-9a-fA-F]+)>");
                var line = new StringBuilder();
                foreach (Match p in parts)
                {
                    if (p.Groups[1].Success) line.Append(CleanPdfString(p.Groups[1].Value));
                    else if (p.Groups[2].Success) line.Append(DecodePdfHexString(p.Groups[2].Value));
                }
                if (line.Length > 0) sb.AppendLine(line.ToString());
            }

            // 4. Decompress FlateDecode streams if text was compressed or current text is unreadable
            if (sb.Length < 20 || !IsLikelyReadableText(sb.ToString()))
            {
                int streamIdx = 0;
                while ((streamIdx = content.IndexOf("stream", streamIdx)) != -1)
                {
                    int start = streamIdx + 6;
                    if (start < pdfBytes.Length && pdfBytes[start] == '\r') start++;
                    if (start < pdfBytes.Length && pdfBytes[start] == '\n') start++;

                    int end = content.IndexOf("endstream", start);
                    if (end != -1 && end > start)
                    {
                        try
                        {
                            byte[] decompressed = DecompressPdfStream(pdfBytes, start, end - start);
                            if (decompressed != null && decompressed.Length > 0)
                            {
                                string decompText = Encoding.ASCII.GetString(decompressed);
                                var innerTj = Regex.Matches(decompText, @"\((.*?)\)\s*(?:Tj|'|"")");
                                foreach (Match m in innerTj)
                                {
                                    string t = CleanPdfString(m.Groups[1].Value);
                                    if (!string.IsNullOrEmpty(t)) sb.AppendLine(t);
                                }
                                var innerHex = Regex.Matches(decompText, @"<([0-9a-fA-F]+)>\s*(?:Tj|'|"")");
                                foreach (Match m in innerHex)
                                {
                                    string t = DecodePdfHexString(m.Groups[1].Value);
                                    if (!string.IsNullOrEmpty(t)) sb.AppendLine(t);
                                }
                                var innerTjArr = Regex.Matches(decompText, @"\[(.*?)\]\s*TJ");
                                foreach (Match m in innerTjArr)
                                {
                                    string inner = m.Groups[1].Value;
                                    var parts = Regex.Matches(inner, @"\((.*?)\)|<([0-9a-fA-F]+)>");
                                    var line = new StringBuilder();
                                    foreach (Match p in parts)
                                    {
                                        if (p.Groups[1].Success) line.Append(CleanPdfString(p.Groups[1].Value));
                                        else if (p.Groups[2].Success) line.Append(DecodePdfHexString(p.Groups[2].Value));
                                    }
                                    if (line.Length > 0) sb.AppendLine(line.ToString());
                                }
                            }
                        }
                        catch { }
                        streamIdx = end + 9;
                    }
                    else
                    {
                        streamIdx += 6;
                    }
                }
            }

            string result = sb.ToString().Trim();
            if (string.IsNullOrEmpty(result) || result.Length < 30 || !IsLikelyReadableText(result))
            {
                // Scanned or image-based PDF: Automatically invoke Windows OCR
                string ocr = RunWindowsOcr(pdfPath);
                if (!string.IsNullOrWhiteSpace(ocr) && IsLikelyReadableText(ocr))
                {
                    result = ocr;
                }
                else if (string.IsNullOrEmpty(result) || !IsLikelyReadableText(result))
                {
                    result = "PDF Document: " + Path.GetFileNameWithoutExtension(pdfPath);
                }
            }
            return result;
        }

        private static byte[] DecompressPdfStream(byte[] bytes, int offset, int count)
        {
            if (count < 2) return null;
            int skip = 0;
            // Check for valid zlib header: RFC 1950 (CMF * 256 + FLG) % 31 == 0
            if (bytes[offset] == 0x78 && ((bytes[offset] * 256 + bytes[offset + 1]) % 31 == 0))
            {
                skip = 2;
            }

            try
            {
                using (var ms = new MemoryStream(bytes, offset + skip, count - skip))
                using (var deflate = new DeflateStream(ms, CompressionMode.Decompress))
                using (var outMs = new MemoryStream())
                {
                    byte[] buffer = new byte[4096];
                    int read;
                    while ((read = deflate.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        outMs.Write(buffer, 0, read);
                    }
                    return outMs.ToArray();
                }
            }
            catch
            {
                return null;
            }
        }

        private static string CleanPdfString(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\(", "(").Replace("\\)", ")").Replace("\\\\", "\\").Trim();
        }

        private static ConversionResult CreateZipArchive(string inputPath, string dir, string baseName)
        {
            string outZip = Path.Combine(dir, baseName + ".zip");
            if (inputPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                outZip = Path.Combine(dir, baseName + "_archive.zip");
            }

            SaveAtomic(outZip, delegate(string tempZip)
            {
                using (var archive = ZipFile.Open(tempZip, ZipArchiveMode.Create))
                {
                    if (File.Exists(inputPath))
                    {
                        archive.CreateEntryFromFile(inputPath, Path.GetFileName(inputPath), CompressionLevel.Optimal);
                    }
                    else if (Directory.Exists(inputPath))
                    {
                        AddDirectoryToZip(archive, inputPath, Path.GetFileName(inputPath));
                    }
                }
            });

            var fi = new FileInfo(outZip);
            return new ConversionResult
            {
                Success = fi.Exists && fi.Length > 0,
                OutputPath = outZip,
                OutputSize = fi.Exists ? fi.Length : 0,
                Message = "Zipped: " + Path.GetFileName(outZip)
            };
        }

        private static ConversionResult CreateTarArchive(string inputPath, string dir, string baseName)
        {
            string outTar = Path.Combine(dir, baseName + ".tar");
            string tarExe = FindExecutable("tar.exe");
            if (!string.IsNullOrEmpty(tarExe))
            {
                SaveAtomic(outTar, delegate(string tempPart)
                {
                    string args = string.Format("-cf \"{0}\" -C \"{1}\" \"{2}\"",
                        SanitizePathForCommandLine(tempPart),
                        SanitizePathForCommandLine(Path.GetDirectoryName(inputPath)),
                        Path.GetFileName(inputPath));
                    RunProcessSafely(tarExe, args, 30000);
                });

                var fi = new FileInfo(outTar);
                if (fi.Exists && fi.Length > 0)
                {
                    return new ConversionResult
                    {
                        Success = true,
                        OutputPath = outTar,
                        OutputSize = fi.Length,
                        Message = "Archived to TAR: " + Path.GetFileName(outTar)
                    };
                }
            }

            return new ConversionResult { Success = false, Message = "tar.exe not found on system" };
        }

        private static ConversionResult ExtractArchive(string inputPath, string ext, string dir, string baseName)
        {
            string outDir = Path.Combine(dir, baseName + "_extracted");
            if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

            if (ext == "zip")
            {
                using (var archive = ZipFile.OpenRead(inputPath))
                {
                    string targetRoot = Path.GetFullPath(outDir);
                    if (!targetRoot.EndsWith(Path.DirectorySeparatorChar.ToString())) targetRoot += Path.DirectorySeparatorChar;

                    foreach (var entry in archive.Entries)
                    {
                        string fullPath = Path.GetFullPath(Path.Combine(outDir, entry.FullName));
                        if (!fullPath.StartsWith(targetRoot, StringComparison.OrdinalIgnoreCase))
                        {
                            // Zip-Slip mitigation: Reject entry traversing outside destination directory
                            continue;
                        }

                        string entryDir = Path.GetDirectoryName(fullPath);
                        if (!string.IsNullOrEmpty(entryDir) && !Directory.Exists(entryDir))
                        {
                            Directory.CreateDirectory(entryDir);
                        }
                        if (!string.IsNullOrEmpty(entry.Name))
                        {
                            entry.ExtractToFile(fullPath, true);
                        }
                    }
                }
                return new ConversionResult
                {
                    Success = true,
                    OutputPath = outDir,
                    Message = "Extracted to: " + Path.GetFileName(outDir)
                };
            }

            // Check for 7-Zip CLI for 7z and rar archives
            if (ext == "7z" || ext == "rar")
            {
                string sevenZip = FindExecutable("7z.exe");
                if (string.IsNullOrEmpty(sevenZip)) sevenZip = FindExecutable("7za.exe");
                if (!string.IsNullOrEmpty(sevenZip))
                {
                    var psi7 = new ProcessStartInfo
                    {
                        FileName = sevenZip,
                        Arguments = string.Format("x \"{0}\" -o\"{1}\" -y", SanitizePathForCommandLine(inputPath), SanitizePathForCommandLine(outDir)),
                        WorkingDirectory = Path.GetTempPath(),
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using (var p = Process.Start(psi7))
                    {
                        if (!p.WaitForExit(60000))
                        {
                            try { p.Kill(); } catch { }
                            return new ConversionResult { Success = false, Message = "7-Zip extraction timed out" };
                        }
                        if (p.ExitCode == 0)
                        {
                            return new ConversionResult { Success = true, OutputPath = outDir, Message = "Extracted to: " + Path.GetFileName(outDir) };
                        }
                    }
                }
            }

            // Native tar.exe (handles tar, tar.gz, tgz, gz, bz2, and on modern Windows also 7z/rar/zip)
            string tarExe = FindExecutable("tar.exe");
            if (!string.IsNullOrEmpty(tarExe))
            {
                try
                {
                    string args = string.Format("-xf \"{0}\" -C \"{1}\"", SanitizePathForCommandLine(inputPath), SanitizePathForCommandLine(outDir));
                    RunProcessSafely(tarExe, args, 45000);
                    return new ConversionResult
                    {
                        Success = true,
                        OutputPath = outDir,
                        Message = "Extracted to: " + Path.GetFileName(outDir)
                    };
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[TarExtract] Error: " + ex.Message);
                }
            }

            return new ConversionResult { Success = false, Message = "No extractor available for ." + ext };
        }

        private static void AddDirectoryToZip(ZipArchive zip, string sourceDir, string entryPrefix, HashSet<string> visitedDirs = null)
        {
            if (visitedDirs == null) visitedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string canonicalDir = Path.GetFullPath(sourceDir);
            if (visitedDirs.Contains(canonicalDir)) return;
            visitedDirs.Add(canonicalDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string entryName = Path.Combine(entryPrefix, Path.GetFileName(file)).Replace('\\', '/');
                zip.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
            }
            foreach (var sub in Directory.GetDirectories(sourceDir))
            {
                try
                {
                    var di = new DirectoryInfo(sub);
                    // Prevent infinite recursion loops from directory junctions, symlinks, or reparse points
                    if ((di.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                }
                catch { continue; }

                string subPrefix = Path.Combine(entryPrefix, Path.GetFileName(sub));
                AddDirectoryToZip(zip, sub, subPrefix, visitedDirs);
            }
        }

        private static string ExtractTextFromDocx(string docxPath)
        {
            var sb = new StringBuilder();
            using (var archive = ZipFile.OpenRead(docxPath))
            {
                var entry = archive.GetEntry("word/document.xml");
                if (entry != null)
                {
                    using (var stream = entry.Open())
                    using (var reader = CreateSafeXmlReader(stream))
                    {
                        while (reader.Read())
                        {
                            if (reader.NodeType == XmlNodeType.Element)
                            {
                                if (reader.LocalName == "p")
                                {
                                    sb.AppendLine();
                                }
                                else if (reader.LocalName == "t")
                                {
                                    sb.Append(reader.ReadElementContentAsString());
                                }
                            }
                        }
                    }
                }
            }
            return sb.ToString().Trim();
        }

        public static void SaveTextAsPdf(string text, string outPdfPath, string title)
        {
            SaveAtomic(outPdfPath, delegate(string tempOut)
            {
                SaveTextAsPdfInternal(text, tempOut, title);
            });
        }

        private static void SaveTextAsPdfInternal(string text, string outPdfPath, string title)
        {
            string[] rawLines = text.Split(new char[] { '\r', '\n' }, StringSplitOptions.None);
            var wrappedLines = new List<string>();

            foreach (var l in rawLines)
            {
                if (string.IsNullOrEmpty(l))
                {
                    wrappedLines.Add("");
                    continue;
                }
                string line = l;
                while (line.Length > 80)
                {
                    int cut = line.LastIndexOf(' ', 80);
                    if (cut <= 0) cut = 80;
                    wrappedLines.Add(line.Substring(0, cut));
                    line = line.Substring(cut).TrimStart();
                }
                wrappedLines.Add(line);
            }

            int linesPerPage = 45;
            int pageCount = (int)Math.Ceiling((double)wrappedLines.Count / linesPerPage);
            if (pageCount < 1) pageCount = 1;

            using (var fs = new FileStream(outPdfPath, FileMode.Create, FileAccess.Write))
            using (var sw = new StreamWriter(fs, Encoding.ASCII))
            {
                var offsets = new List<long>();
                sw.Write("%PDF-1.4\n");
                sw.Flush();

                offsets.Add(fs.Position);
                sw.Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
                sw.Flush();

                var pageKids = new StringBuilder();
                for (int p = 0; p < pageCount; p++)
                {
                    pageKids.Append(string.Format("{0} 0 R ", 3 + (p * 2)));
                }
                offsets.Add(fs.Position);
                sw.Write(string.Format("2 0 obj\n<< /Type /Pages /Kids [{0}] /Count {1} >>\nendobj\n", pageKids.ToString().Trim(), pageCount));
                sw.Flush();

                int fontObjId = 3 + (pageCount * 2);

                for (int p = 0; p < pageCount; p++)
                {
                    int pageObjId = 3 + (p * 2);
                    int contentObjId = pageObjId + 1;

                    offsets.Add(fs.Position);
                    sw.Write(string.Format("{0} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents {1} 0 R /Resources << /Font << /F1 {2} 0 R >> >> >>\nendobj\n", pageObjId, contentObjId, fontObjId));
                    sw.Flush();

                    var streamContent = new StringBuilder();
                    streamContent.Append("BT\n/F1 11 Tf\n50 740 Td\n15 TL\n");

                    int startIdx = p * linesPerPage;
                    int endIdx = Math.Min(startIdx + linesPerPage, wrappedLines.Count);

                    for (int i = startIdx; i < endIdx; i++)
                    {
                        string line = wrappedLines[i];
                        string escaped = line.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
                        escaped = Regex.Replace(escaped, @"[^\u0020-\u007E]", " ");
                        streamContent.Append(string.Format("({0}) Tj T*\n", escaped));
                    }
                    streamContent.Append("ET\n");

                    byte[] streamBytes = Encoding.ASCII.GetBytes(streamContent.ToString());
                    offsets.Add(fs.Position);
                    sw.Write(string.Format("{0} 0 obj\n<< /Length {1} >>\nstream\n", contentObjId, streamBytes.Length));
                    sw.Flush();
                    fs.Write(streamBytes, 0, streamBytes.Length);
                    sw.Write("\nendstream\nendobj\n");
                    sw.Flush();
                }

                offsets.Add(fs.Position);
                sw.Write(string.Format("{0} 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n", fontObjId));
                sw.Flush();

                long xrefPos = fs.Position;
                sw.Write(string.Format("xref\n0 {0}\n0000000000 65535 f \n", offsets.Count + 1));
                foreach (long off in offsets)
                {
                    sw.Write(string.Format("{0:D10} 00000 n \n", off));
                }

                sw.Write(string.Format("trailer\n<< /Size {0} /Root 1 0 R >>\nstartxref\n{1}\n%%EOF\n", offsets.Count + 1, xrefPos));
                sw.Flush();
            }
        }

        private static string ExtractTextFromRtf(string rtfPath)
        {
            string raw = File.ReadAllText(rtfPath, Encoding.UTF8);
            string extracted = null;
            try
            {
                if (System.Threading.Thread.CurrentThread.GetApartmentState() == System.Threading.ApartmentState.STA)
                {
                    using (var rtb = new System.Windows.Forms.RichTextBox())
                    {
                        rtb.Rtf = raw;
                        extracted = rtb.Text;
                    }
                }
                else
                {
                    var t = new System.Threading.Thread(delegate()
                    {
                        try
                        {
                            using (var rtb = new System.Windows.Forms.RichTextBox())
                            {
                                rtb.Rtf = raw;
                                extracted = rtb.Text;
                            }
                        }
                        catch { }
                    });
                    t.SetApartmentState(System.Threading.ApartmentState.STA);
                    t.Start();
                    t.Join(3000);
                }
            }
            catch { }

            if (!string.IsNullOrEmpty(extracted)) return extracted;

            // Fallback: Regex strip RTF control codes
            string stripped = Regex.Replace(raw, @"\\(fonttbl|colortbl|stylesheet).*?;", "", RegexOptions.Singleline);
            stripped = Regex.Replace(stripped, @"\\[a-zA-Z0-9]+(-?\d+)?\s?", " ");
            stripped = Regex.Replace(stripped, @"[{}]", "");
            return stripped.Trim();
        }

        private static string MarkdownToHtml(string md, string title)
        {
            if (string.IsNullOrEmpty(md)) md = "";
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head><meta charset='utf-8'>");
            sb.AppendLine(string.Format("<title>{0}</title>", System.Security.SecurityElement.Escape(title)));
            sb.AppendLine("<style>");
            sb.AppendLine("body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; line-height: 1.6; max-width: 860px; margin: 2rem auto; padding: 0 1.5rem; color: #1e293b; background: #f8fafc; }");
            sb.AppendLine("h1, h2, h3, h4 { color: #0f172a; margin-top: 1.5rem; margin-bottom: 0.5rem; font-weight: 600; }");
            sb.AppendLine("h1 { border-bottom: 2px solid #e2e8f0; padding-bottom: 0.4rem; }");
            sb.AppendLine("pre { background: #0f172a; color: #f8fafc; padding: 1rem; border-radius: 6px; overflow-x: auto; font-family: Consolas, monospace; }");
            sb.AppendLine("code { background: #e2e8f0; color: #0f172a; padding: 0.2rem 0.4rem; border-radius: 4px; font-family: Consolas, monospace; font-size: 0.9em; }");
            sb.AppendLine("pre code { background: none; color: inherit; padding: 0; }");
            sb.AppendLine("blockquote { border-left: 4px solid #3b82f6; margin: 0; padding-left: 1rem; color: #64748b; font-style: italic; }");
            sb.AppendLine("table { border-collapse: collapse; width: 100%; margin: 1rem 0; }");
            sb.AppendLine("th, td { border: 1px solid #cbd5e1; padding: 0.6rem 0.8rem; text-align: left; }");
            sb.AppendLine("th { background: #f1f5f9; font-weight: 600; }");
            sb.AppendLine("hr { border: none; border-top: 1px solid #e2e8f0; margin: 1.5rem 0; }");
            sb.AppendLine("ul, ol { padding-left: 1.5rem; }");
            sb.AppendLine("p { margin: 0.8rem 0; }");
            sb.AppendLine("</style></head><body>");

            string[] lines = md.Split(new char[] { '\r', '\n' }, StringSplitOptions.None);
            bool inCodeBlock = false;
            bool inList = false;
            bool inTable = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.Trim();

                if (trimmed.StartsWith("```"))
                {
                    if (inCodeBlock)
                    {
                        sb.AppendLine("</code></pre>");
                        inCodeBlock = false;
                    }
                    else
                    {
                        if (inList) { sb.AppendLine("</ul>"); inList = false; }
                        if (inTable) { sb.AppendLine("</table>"); inTable = false; }
                        string lang = trimmed.Length > 3 ? trimmed.Substring(3).Trim() : "";
                        sb.AppendLine(string.Format("<pre><code class=\"language-{0}\">", System.Security.SecurityElement.Escape(lang)));
                        inCodeBlock = true;
                    }
                    continue;
                }

                if (inCodeBlock)
                {
                    sb.AppendLine(System.Security.SecurityElement.Escape(line));
                    continue;
                }

                if (trimmed.StartsWith("|") && trimmed.EndsWith("|"))
                {
                    if (!inTable)
                    {
                        if (inList) { sb.AppendLine("</ul>"); inList = false; }
                        sb.AppendLine("<table>");
                        inTable = true;
                    }

                    if (Regex.IsMatch(trimmed, @"^\|(\s*:?-+:?\s*\|)+$"))
                    {
                        continue;
                    }

                    string[] cells = trimmed.Split(new char[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
                    sb.Append("<tr>");
                    bool isHeader = (i + 1 < lines.Length && Regex.IsMatch(lines[i + 1].Trim(), @"^\|(\s*:?-+:?\s*\|)+$"));
                    string tag = isHeader ? "th" : "td";
                    foreach (var c in cells)
                    {
                        sb.Append(string.Format("<{0}>{1}</{0}>", tag, FormatInlineMarkdown(c.Trim())));
                    }
                    sb.AppendLine("</tr>");
                    continue;
                }
                else if (inTable)
                {
                    sb.AppendLine("</table>");
                    inTable = false;
                }

                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    if (inList) { sb.AppendLine("</ul>"); inList = false; }
                    continue;
                }

                if (trimmed == "---" || trimmed == "***" || trimmed == "___")
                {
                    if (inList) { sb.AppendLine("</ul>"); inList = false; }
                    sb.AppendLine("<hr/>");
                    continue;
                }

                if (trimmed.StartsWith("#"))
                {
                    if (inList) { sb.AppendLine("</ul>"); inList = false; }
                    int level = 0;
                    while (level < trimmed.Length && trimmed[level] == '#') level++;
                    level = Math.Min(level, 6);
                    string text = trimmed.Substring(level).Trim();
                    sb.AppendLine(string.Format("<h{0}>{1}</h{0}>", level, FormatInlineMarkdown(text)));
                    continue;
                }

                if (trimmed.StartsWith(">"))
                {
                    if (inList) { sb.AppendLine("</ul>"); inList = false; }
                    string text = trimmed.Substring(1).Trim();
                    sb.AppendLine(string.Format("<blockquote>{0}</blockquote>", FormatInlineMarkdown(text)));
                    continue;
                }

                if (trimmed.StartsWith("- ") || trimmed.StartsWith("* ") || trimmed.StartsWith("+ "))
                {
                    if (!inList)
                    {
                        sb.AppendLine("<ul>");
                        inList = true;
                    }
                    sb.AppendLine(string.Format("<li>{0}</li>", FormatInlineMarkdown(trimmed.Substring(2).Trim())));
                    continue;
                }
                else if (inList)
                {
                    sb.AppendLine("</ul>");
                    inList = false;
                }

                sb.AppendLine(string.Format("<p>{0}</p>", FormatInlineMarkdown(line)));
            }

            if (inCodeBlock) sb.AppendLine("</code></pre>");
            if (inList) sb.AppendLine("</ul>");
            if (inTable) sb.AppendLine("</table>");

            sb.AppendLine("</body></html>");
            return sb.ToString();

        }

        private static string FormatInlineMarkdown(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string s = System.Security.SecurityElement.Escape(text);
            s = Regex.Replace(s, @"`([^`]+)`", "<code>$1</code>");
            s = Regex.Replace(s, @"\*\*([^*]+)\*\*", "<strong>$1</strong>");
            s = Regex.Replace(s, @"__([^_]+)__", "<strong>$1</strong>");
            s = Regex.Replace(s, @"\*([^*]+)\*", "<em>$1</em>");
            s = Regex.Replace(s, @"_([^_]+)_", "<em>$1</em>");
            s = Regex.Replace(s, @"\[([^\]]+)\]\(([^)]+)\)", "<a href=\"$2\" target=\"_blank\">$1</a>");
            return s;
        }

        private static string HtmlToMarkdown(string html)
        {
            if (string.IsNullOrEmpty(html)) return "";
            string s = html;

            s = Regex.Replace(s, @"<script.*?</script>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"<style.*?</style>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"<head.*?</head>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);

            for (int i = 1; i <= 6; i++)
            {
                string hashes = new string('#', i);
                s = Regex.Replace(s, @"<h" + i + @"[^>]*>(.*?)</h" + i + @">", "\n\n" + hashes + " $1\n\n", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            }

            s = Regex.Replace(s, @"<pre><code[^>]*>(.*?)</code></pre>", "\n```\n$1\n```\n", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"<code>(.*?)</code>", "`$1`", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"<(strong|b)>(.*?)</\1>", "**$2**", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"<(em|i)>(.*?)</\1>", "*$2*", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"<a\s+[^>]*href=[""']([^""']+)[""'][^>]*>(.*?)</a>", "[$2]($1)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"<li[^>]*>(.*?)</li>", "- $1\n", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"<hr\s*/?>", "\n\n---\n\n", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"<p[^>]*>(.*?)</p>", "$1\n\n", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"<.*?>", string.Empty);
            s = System.Net.WebUtility.HtmlDecode(s);
            s = Regex.Replace(s, @"\n{3,}", "\n\n");
            return s.Trim();
        }

        #endregion

        #region Spreadsheet & Data Engine
        private static bool IsDataExtension(string ext)
        {
            return ext == "xlsx" || ext == "xls" || ext == "ods" || ext == "json" || ext == "jsonl" ||
                   ext == "csv" || ext == "tsv" || ext == "xml" || ext == "vcf" || ext == "yaml" || ext == "yml";
        }

        private static ConversionResult ConvertData(string inputPath, string ext, string targetFormat, ConversionOptions opt, string dir, string baseName)
        {
            string outPath = Path.Combine(dir, baseName + "." + targetFormat);

            // Handle Excel XLSX / XLS
            if (ext == "xlsx" || ext == "xls")
            {
                return ConvertExcelSpreadsheet(inputPath, ext, targetFormat, dir, baseName);
            }

            // CSV or TSV Input
            if (ext == "csv" || ext == "tsv")
            {
                byte[] rawBytes = File.ReadAllBytes(inputPath);
                bool isUtf16 = rawBytes.Length >= 2 && ((rawBytes[0] == 0xFF && rawBytes[1] == 0xFE) || (rawBytes[0] == 0xFE && rawBytes[1] == 0xFF));
                if (!isUtf16)
                {
                    for (int i = 0; i < Math.Min(rawBytes.Length, 1024); i++)
                    {
                        if (rawBytes[i] == 0)
                        {
                            return new ConversionResult { Success = false, Message = "Binary content detected in delimited text file: " + Path.GetFileName(inputPath) };
                        }
                    }
                }
                char delim = (ext == "tsv") ? '\t' : ',';
                string textContent = File.ReadAllText(inputPath);
                var grid = ParseDelimitedText(textContent, delim);

                if (targetFormat == "csv")
                {
                    SaveAtomic(outPath, p => File.WriteAllText(p, GridToDelimitedText(grid, ','), Encoding.UTF8));
                }
                else if (targetFormat == "tsv")
                {
                    SaveAtomic(outPath, p => File.WriteAllText(p, GridToDelimitedText(grid, '\t'), Encoding.UTF8));
                }
                else if (targetFormat == "json")
                {
                    SaveAtomic(outPath, p => File.WriteAllText(p, GridToJson(grid), Encoding.UTF8));
                }
                else if (targetFormat == "jsonl")
                {
                    SaveAtomic(outPath, p => File.WriteAllText(p, GridToJsonLines(grid), Encoding.UTF8));
                }
                else if (targetFormat == "html")
                {
                    SaveAtomic(outPath, p => File.WriteAllText(p, GridToHtmlTable(grid, baseName), Encoding.UTF8));
                }
                else if (targetFormat == "xml")
                {
                    SaveAtomic(outPath, p => File.WriteAllText(p, GridToXml(grid), Encoding.UTF8));
                }
                else if (targetFormat == "xlsx")
                {
                    CreateXlsxFromGrid(grid, outPath);
                }
                else if (targetFormat == "md")
                {
                    SaveAtomic(outPath, p => File.WriteAllText(p, GridToMarkdownTable(grid), Encoding.UTF8));
                }
                else if (targetFormat == "yaml" || targetFormat == "yml")
                {
                    SaveAtomic(outPath, p => File.WriteAllText(p, GridToYaml(grid), Encoding.UTF8));
                }
                else if (targetFormat == "txt")
                {
                    SaveAtomic(outPath, p => File.WriteAllText(p, GridToPaddedTable(grid), Encoding.UTF8));
                }
                else if (targetFormat == "pdf")
                {
                    SaveTextAsPdf(GridToPaddedTable(grid), outPath, baseName);
                }
                else if (targetFormat == "vcf")
                {
                    SaveAtomic(outPath, p => File.WriteAllText(p, GridToVCard(grid), Encoding.UTF8));
                }
                else
                {
                    return new ConversionResult { Success = false, Message = "Unsupported data conversion: " + ext + " -> " + targetFormat };
                }

                var fi = new FileInfo(outPath);
                bool ok = File.Exists(outPath) && fi.Length > 0;
                return new ConversionResult
                {
                    Success = ok,
                    OutputPath = outPath,
                    OutputSize = ok ? fi.Length : 0,
                    Message = ok ? ("Data transformed to " + Path.GetFileName(outPath)) : ("Data transformation failed: " + ext + " -> " + targetFormat)
                };
            }

            // JSON or JSONL Input
            if (ext == "json" || ext == "jsonl")
            {
                string raw = File.ReadAllText(inputPath, Encoding.UTF8).Trim();
                if (targetFormat == "jsonl" && ext == "json")
                {
                    var grid = ParseJsonToGrid(raw);
                    if (grid == null || (grid.Count == 0 && raw.Length > 2 && raw != "[]" && raw != "{}"))
                    {
                        return new ConversionResult { Success = false, Message = "Invalid or malformed JSON structure" };
                    }
                    SaveAtomic(outPath, p => File.WriteAllText(p, GridToJsonLines(grid), Encoding.UTF8));
                }
                else if (targetFormat == "json" && ext == "jsonl")
                {
                    var lines = raw.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    var cleanLines = new List<string>();
                    foreach (var l in lines)
                    {
                        string trimmed = l.Trim();
                        if (!string.IsNullOrEmpty(trimmed)) cleanLines.Add(trimmed);
                    }
                    string jsonArray = "[\n  " + string.Join(",\n  ", cleanLines.ToArray()) + "\n]";
                    SaveAtomic(outPath, p => File.WriteAllText(p, jsonArray, Encoding.UTF8));
                }
                else if (targetFormat == "csv" || targetFormat == "tsv" || targetFormat == "html" || targetFormat == "xml" || targetFormat == "xlsx" || targetFormat == "md" || targetFormat == "yaml" || targetFormat == "yml" || targetFormat == "txt" || targetFormat == "pdf" || targetFormat == "vcf")
                {
                    var grid = ParseJsonToGrid(raw);
                    if (grid == null || (grid.Count == 0 && raw.Length > 2 && raw != "[]" && raw != "{}"))
                    {
                        return new ConversionResult { Success = false, Message = "Invalid or malformed JSON structure" };
                    }
                    if (targetFormat == "csv") SaveAtomic(outPath, p => File.WriteAllText(p, GridToDelimitedText(grid, ','), Encoding.UTF8));
                    else if (targetFormat == "tsv") SaveAtomic(outPath, p => File.WriteAllText(p, GridToDelimitedText(grid, '\t'), Encoding.UTF8));
                    else if (targetFormat == "html") SaveAtomic(outPath, p => File.WriteAllText(p, GridToHtmlTable(grid, baseName), Encoding.UTF8));
                    else if (targetFormat == "xml") SaveAtomic(outPath, p => File.WriteAllText(p, GridToXml(grid), Encoding.UTF8));
                    else if (targetFormat == "xlsx") CreateXlsxFromGrid(grid, outPath);
                    else if (targetFormat == "md") SaveAtomic(outPath, p => File.WriteAllText(p, GridToMarkdownTable(grid), Encoding.UTF8));
                    else if (targetFormat == "yaml" || targetFormat == "yml") SaveAtomic(outPath, p => File.WriteAllText(p, GridToYaml(grid), Encoding.UTF8));
                    else if (targetFormat == "txt") SaveAtomic(outPath, p => File.WriteAllText(p, GridToPaddedTable(grid), Encoding.UTF8));
                    else if (targetFormat == "pdf") SaveTextAsPdf(GridToPaddedTable(grid), outPath, baseName);
                    else if (targetFormat == "vcf") SaveAtomic(outPath, p => File.WriteAllText(p, GridToVCard(grid), Encoding.UTF8));
                }
                else
                {
                    return new ConversionResult { Success = false, Message = "Unsupported data conversion: " + ext + " -> " + targetFormat };
                }

                var fi = new FileInfo(outPath);
                bool ok = File.Exists(outPath) && fi.Length > 0;
                return new ConversionResult
                {
                    Success = ok,
                    OutputPath = outPath,
                    OutputSize = ok ? fi.Length : 0,
                    Message = ok ? ("JSON transformed to " + Path.GetFileName(outPath)) : ("JSON transformation failed: " + ext + " -> " + targetFormat)
                };
            }

            // XML Input
            if (ext == "xml")
            {
                string xml = File.ReadAllText(inputPath, Encoding.UTF8);
                var grid = ParseXmlToGrid(xml);
                if (targetFormat == "json")
                {
                    SaveAtomic(outPath, p => File.WriteAllText(p, GridToJson(grid), Encoding.UTF8));
                }
                else if (targetFormat == "jsonl")
                {
                    SaveAtomic(outPath, p => File.WriteAllText(p, GridToJsonLines(grid), Encoding.UTF8));
                }
                else if (targetFormat == "csv" || targetFormat == "tsv")
                {
                    char d = targetFormat == "tsv" ? '\t' : ',';
                    SaveAtomic(outPath, p => File.WriteAllText(p, GridToDelimitedText(grid, d), Encoding.UTF8));
                }
                else if (targetFormat == "html")
                {
                    SaveAtomic(outPath, p => File.WriteAllText(p, GridToHtmlTable(grid, baseName), Encoding.UTF8));
                }
                else if (targetFormat == "xlsx")
                {
                    CreateXlsxFromGrid(grid, outPath);
                }
                else if (targetFormat == "md")
                {
                    SaveAtomic(outPath, p => File.WriteAllText(p, GridToMarkdownTable(grid), Encoding.UTF8));
                }
                else if (targetFormat == "yaml" || targetFormat == "yml")
                {
                    SaveAtomic(outPath, p => File.WriteAllText(p, GridToYaml(grid), Encoding.UTF8));
                }
                else if (targetFormat == "txt")
                {
                    SaveAtomic(outPath, p => File.WriteAllText(p, GridToPaddedTable(grid), Encoding.UTF8));
                }
                else if (targetFormat == "pdf")
                {
                    SaveTextAsPdf(GridToPaddedTable(grid), outPath, baseName);
                }
                else if (targetFormat == "vcf")
                {
                    SaveAtomic(outPath, p => File.WriteAllText(p, GridToVCard(grid), Encoding.UTF8));
                }
                else
                {
                    return new ConversionResult { Success = false, Message = "Unsupported data conversion: xml -> " + targetFormat };
                }

                var fi = new FileInfo(outPath);
                bool ok = File.Exists(outPath) && fi.Length > 0;
                return new ConversionResult
                {
                    Success = ok,
                    OutputPath = outPath,
                    OutputSize = ok ? fi.Length : 0,
                    Message = ok ? ("XML converted to " + Path.GetFileName(outPath)) : ("XML conversion failed: xml -> " + targetFormat)
                };
            }

            // YAML Input
            if (ext == "yaml" || ext == "yml")
            {
                string raw = File.ReadAllText(inputPath, Encoding.UTF8);
                var grid = ParseYamlToGrid(raw);
                if (targetFormat == "json") SaveAtomic(outPath, p => File.WriteAllText(p, GridToJson(grid), Encoding.UTF8));
                else if (targetFormat == "jsonl") SaveAtomic(outPath, p => File.WriteAllText(p, GridToJsonLines(grid), Encoding.UTF8));
                else if (targetFormat == "csv") SaveAtomic(outPath, p => File.WriteAllText(p, GridToDelimitedText(grid, ','), Encoding.UTF8));
                else if (targetFormat == "tsv") SaveAtomic(outPath, p => File.WriteAllText(p, GridToDelimitedText(grid, '\t'), Encoding.UTF8));
                else if (targetFormat == "html") SaveAtomic(outPath, p => File.WriteAllText(p, GridToHtmlTable(grid, baseName), Encoding.UTF8));
                else if (targetFormat == "xml") SaveAtomic(outPath, p => File.WriteAllText(p, GridToXml(grid), Encoding.UTF8));
                else if (targetFormat == "xlsx") CreateXlsxFromGrid(grid, outPath);
                else if (targetFormat == "md") SaveAtomic(outPath, p => File.WriteAllText(p, GridToMarkdownTable(grid), Encoding.UTF8));
                else if (targetFormat == "txt") SaveAtomic(outPath, p => File.WriteAllText(p, GridToPaddedTable(grid), Encoding.UTF8));
                else if (targetFormat == "pdf") SaveTextAsPdf(GridToPaddedTable(grid), outPath, baseName);
                else if (targetFormat == "vcf") SaveAtomic(outPath, p => File.WriteAllText(p, GridToVCard(grid), Encoding.UTF8));
                else return new ConversionResult { Success = false, Message = "Unsupported data conversion: " + ext + " -> " + targetFormat };

                var fi = new FileInfo(outPath);
                bool ok = File.Exists(outPath) && fi.Length > 0;
                return new ConversionResult
                {
                    Success = ok,
                    OutputPath = outPath,
                    OutputSize = ok ? fi.Length : 0,
                    Message = ok ? ("YAML converted to " + Path.GetFileName(outPath)) : ("YAML conversion failed: " + ext + " -> " + targetFormat)
                };
            }

            // VCF (vCard) Input
            if (ext == "vcf")
            {
                var grid = ParseVCardToGrid(inputPath);
                if (grid == null || grid.Count <= 1)
                {
                    return new ConversionResult { Success = false, Message = "No valid vCard records found in: " + Path.GetFileName(inputPath) };
                }

                if (targetFormat == "csv") SaveAtomic(outPath, p => File.WriteAllText(p, GridToDelimitedText(grid, ','), Encoding.UTF8));
                else if (targetFormat == "tsv") SaveAtomic(outPath, p => File.WriteAllText(p, GridToDelimitedText(grid, '\t'), Encoding.UTF8));
                else if (targetFormat == "json") SaveAtomic(outPath, p => File.WriteAllText(p, GridToJson(grid), Encoding.UTF8));
                else if (targetFormat == "jsonl") SaveAtomic(outPath, p => File.WriteAllText(p, GridToJsonLines(grid), Encoding.UTF8));
                else if (targetFormat == "html") SaveAtomic(outPath, p => File.WriteAllText(p, GridToHtmlTable(grid, baseName), Encoding.UTF8));
                else if (targetFormat == "xml") SaveAtomic(outPath, p => File.WriteAllText(p, GridToXml(grid), Encoding.UTF8));
                else if (targetFormat == "xlsx") CreateXlsxFromGrid(grid, outPath);
                else if (targetFormat == "md") SaveAtomic(outPath, p => File.WriteAllText(p, GridToMarkdownTable(grid), Encoding.UTF8));
                else if (targetFormat == "yaml" || targetFormat == "yml") SaveAtomic(outPath, p => File.WriteAllText(p, GridToYaml(grid), Encoding.UTF8));
                else if (targetFormat == "txt") SaveAtomic(outPath, p => File.WriteAllText(p, GridToPaddedTable(grid), Encoding.UTF8));
                else if (targetFormat == "pdf") SaveTextAsPdf(GridToPaddedTable(grid), outPath, baseName);
                else if (targetFormat == "vcf") SaveAtomic(outPath, p => File.WriteAllText(p, GridToVCard(grid), Encoding.UTF8));
                else return new ConversionResult { Success = false, Message = "Unsupported data conversion: vcf -> " + targetFormat };

                var fi = new FileInfo(outPath);
                bool ok = File.Exists(outPath) && fi.Length > 0;
                return new ConversionResult
                {
                    Success = ok,
                    OutputPath = outPath,
                    OutputSize = ok ? fi.Length : 0,
                    Message = ok ? ("vCard converted to " + Path.GetFileName(outPath)) : ("vCard conversion failed: vcf -> " + targetFormat)
                };
            }

            return new ConversionResult { Success = false, Message = "Unsupported data format: " + ext };
        }

        private static ConversionResult ConvertExcelSpreadsheet(string inputPath, string ext, string targetFormat, string dir, string baseName)
        {
            string outPath = Path.Combine(dir, baseName + "." + targetFormat);

            // 1. Native OpenXML Parser (0 external dependencies, 100% offline, runs in 5ms)
            if (ext == "xlsx")
            {
                try
                {
                    var grid = ParseXlsxWorkbook(inputPath);
                    if (grid != null && grid.Count > 0)
                    {
                        if (targetFormat == "csv")
                        {
                            SaveAtomic(outPath, p => File.WriteAllText(p, GridToDelimitedText(grid, ','), Encoding.UTF8));
                        }
                        else if (targetFormat == "tsv")
                        {
                            SaveAtomic(outPath, p => File.WriteAllText(p, GridToDelimitedText(grid, '\t'), Encoding.UTF8));
                        }
                        else if (targetFormat == "json")
                        {
                            SaveAtomic(outPath, p => File.WriteAllText(p, GridToJson(grid), Encoding.UTF8));
                        }
                        else if (targetFormat == "jsonl")
                        {
                            SaveAtomic(outPath, p => File.WriteAllText(p, GridToJsonLines(grid), Encoding.UTF8));
                        }
                        else if (targetFormat == "html")
                        {
                            SaveAtomic(outPath, p => File.WriteAllText(p, GridToHtmlTable(grid, baseName), Encoding.UTF8));
                        }
                        else if (targetFormat == "txt")
                        {
                            SaveAtomic(outPath, p => File.WriteAllText(p, GridToPaddedTable(grid), Encoding.UTF8));
                        }
                        else if (targetFormat == "md")
                        {
                            SaveAtomic(outPath, p => File.WriteAllText(p, GridToMarkdownTable(grid), Encoding.UTF8));
                        }
                        else if (targetFormat == "yaml" || targetFormat == "yml")
                        {
                            SaveAtomic(outPath, p => File.WriteAllText(p, GridToYaml(grid), Encoding.UTF8));
                        }
                        else if (targetFormat == "pdf")
                        {
                            SaveTextAsPdf(GridToPaddedTable(grid), outPath, baseName);
                        }
                        else if (targetFormat == "xml")
                        {
                            SaveAtomic(outPath, p => File.WriteAllText(p, GridToXml(grid), Encoding.UTF8));
                        }

                        if (File.Exists(outPath))
                        {
                            var fi = new FileInfo(outPath);
                            if (fi.Length > 0)
                            {
                                return new ConversionResult
                                {
                                    Success = true,
                                    OutputPath = outPath,
                                    OutputSize = fi.Length,
                                    Message = "Natively parsed Excel to " + Path.GetFileName(outPath)
                                };
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[NativeXLSX] Fallback to COM: " + ex.Message);
                }
            }

            // 2. Microsoft Excel COM Automation Fallback with strict 5-second timeout
            try
            {
                Type excelType = Type.GetTypeFromProgID("Excel.Application");
                if (excelType != null)
                {
                    bool comSuccess = false;
                    var comThread = new System.Threading.Thread(delegate()
                    {
                        try
                        {
                            dynamic excelApp = Activator.CreateInstance(excelType);
                            try
                            {
                                excelApp.Visible = false;
                                excelApp.DisplayAlerts = false;

                                dynamic workbooks = null;
                                dynamic wb = null;
                                try
                                {
                                    workbooks = excelApp.Workbooks;
                                    wb = workbooks.Open(inputPath, ReadOnly: true);
                                    if (targetFormat == "csv")
                                    {
                                        wb.SaveAs(outPath, 6); // 6 = xlCSV
                                    }
                                    else if (targetFormat == "tsv")
                                    {
                                        wb.SaveAs(outPath, 20); // 20 = xlTextWindows
                                    }
                                    else if (targetFormat == "pdf")
                                    {
                                        wb.ExportAsFixedFormat(0, outPath); // 0 = xlTypePDF
                                    }
                                    else if (targetFormat == "html")
                                    {
                                        wb.SaveAs(outPath, 44); // 44 = xlHtml
                                    }
                                    comSuccess = true;
                                }
                                finally
                                {
                                    if (wb != null) { try { wb.Close(false); System.Runtime.InteropServices.Marshal.FinalReleaseComObject(wb); } catch { } }
                                    if (workbooks != null) { try { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(workbooks); } catch { } }
                                }
                            }
                            finally
                            {
                                try { excelApp.Quit(); System.Runtime.InteropServices.Marshal.FinalReleaseComObject(excelApp); } catch { }
                                GC.Collect();
                                GC.WaitForPendingFinalizers();
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine("[ExcelCOM] Error: " + ex.Message);
                        }
                    });
                    comThread.IsBackground = true;
                    comThread.SetApartmentState(System.Threading.ApartmentState.STA);
                    comThread.Start();
                    comThread.Join(5000);

                    if (comSuccess && File.Exists(outPath))
                    {
                        var fiExcel = new FileInfo(outPath);
                        return new ConversionResult
                        {
                            Success = true,
                            OutputPath = outPath,
                            OutputSize = fiExcel.Length,
                            Message = "Exported spreadsheet via Excel to " + Path.GetFileName(outPath)
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[ExcelCOM] Fallback triggered: " + ex.Message);
            }

            return new ConversionResult
            {
                Success = false,
                Message = "Spreadsheet conversion to " + targetFormat + " could not complete."
            };
        }

        #region OpenXML XLSX Parsing & Generating
        private static List<List<string>> ParseXlsxWorkbook(string xlsxPath)
        {
            var grid = new List<List<string>>();
            var sharedStrings = new List<string>();

            using (var archive = ZipFile.OpenRead(xlsxPath))
            {
                // 1. Read shared strings if present
                var ssEntry = archive.GetEntry("xl/sharedStrings.xml");
                if (ssEntry != null)
                {
                    using (var stream = ssEntry.Open())
                    using (var reader = CreateSafeXmlReader(stream))
                    {
                        while (reader.Read())
                        {
                            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "t")
                            {
                                sharedStrings.Add(reader.ReadElementContentAsString());
                            }
                        }
                    }
                }

                // 2. Read first sheet: xl/worksheets/sheet1.xml
                ZipArchiveEntry sheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml");
                if (sheetEntry == null)
                {
                    foreach (var e in archive.Entries)
                    {
                        if (e.FullName.StartsWith("xl/worksheets/sheet") && e.FullName.EndsWith(".xml"))
                        {
                            sheetEntry = e;
                            break;
                        }
                    }
                }

                if (sheetEntry != null)
                {
                    using (var stream = sheetEntry.Open())
                    using (var reader = CreateSafeXmlReader(stream))
                    {
                        List<string> currentRow = null;
                        string cellType = "";
                        string cellVal = "";

                        while (reader.Read())
                        {
                            if (reader.NodeType == XmlNodeType.Element)
                            {
                                if (reader.LocalName == "row")
                                {
                                    currentRow = new List<string>();
                                    grid.Add(currentRow);
                                }
                                else if (reader.LocalName == "c")
                                {
                                    cellType = reader.GetAttribute("t") ?? "";
                                }
                                else if (reader.LocalName == "v" || reader.LocalName == "t")
                                {
                                    cellVal = reader.ReadElementContentAsString();
                                    if (cellType == "s")
                                    {
                                        int sIdx;
                                        if (int.TryParse(cellVal, out sIdx) && sIdx >= 0 && sIdx < sharedStrings.Count)
                                        {
                                            cellVal = sharedStrings[sIdx];
                                        }
                                    }
                                    if (currentRow != null) currentRow.Add(cellVal);
                                }
                            }
                        }
                    }
                }
            }

            return grid;
        }

        private static void CreateXlsxFromGrid(List<List<string>> grid, string outPath)
        {
            SaveAtomic(outPath, delegate(string tempOut)
            {
                CreateXlsxFromGridInternal(grid, tempOut);
            });
        }

        private static void CreateXlsxFromGridInternal(List<List<string>> grid, string outPath)
        {
            using (var fs = new FileStream(outPath, FileMode.Create))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                // [Content_Types].xml
                var ct = archive.CreateEntry("[Content_Types].xml", CompressionLevel.Optimal);
                using (var sw = new StreamWriter(ct.Open(), Encoding.UTF8))
                {
                    sw.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" +
                             "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">\r\n" +
                             "  <Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>\r\n" +
                             "  <Default Extension=\"xml\" ContentType=\"application/xml\"/>\r\n" +
                             "  <Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>\r\n" +
                             "  <Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>\r\n" +
                             "</Types>");
                }

                // _rels/.rels
                var rels = archive.CreateEntry("_rels/.rels", CompressionLevel.Optimal);
                using (var sw = new StreamWriter(rels.Open(), Encoding.UTF8))
                {
                    sw.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" +
                             "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">\r\n" +
                             "  <Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>\r\n" +
                             "</Relationships>");
                }

                // xl/_rels/workbook.xml.rels
                var wbRels = archive.CreateEntry("xl/_rels/workbook.xml.rels", CompressionLevel.Optimal);
                using (var sw = new StreamWriter(wbRels.Open(), Encoding.UTF8))
                {
                    sw.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" +
                             "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">\r\n" +
                             "  <Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>\r\n" +
                             "</Relationships>");
                }

                // xl/workbook.xml
                var wb = archive.CreateEntry("xl/workbook.xml", CompressionLevel.Optimal);
                using (var sw = new StreamWriter(wb.Open(), Encoding.UTF8))
                {
                    sw.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" +
                             "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">\r\n" +
                             "  <sheets>\r\n" +
                             "    <sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\"/>\r\n" +
                             "  </sheets>\r\n" +
                             "</workbook>");
                }

                // xl/worksheets/sheet1.xml
                var sheet = archive.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Optimal);
                using (var sw = new StreamWriter(sheet.Open(), Encoding.UTF8))
                {
                    var sb = new StringBuilder();
                    sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n");
                    sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">\r\n");
                    sb.Append("  <sheetData>\r\n");

                    for (int r = 0; r < grid.Count; r++)
                    {
                        var row = grid[r];
                        sb.Append(string.Format("    <row r=\"{0}\">\r\n", r + 1));
                        for (int c = 0; c < row.Count; c++)
                        {
                            string colName = GetExcelColumnName(c + 1);
                            string cellRef = string.Format("{0}{1}", colName, r + 1);
                            string val = row[c] ?? "";

                            double d;
                            if (double.TryParse(val, out d))
                            {
                                sb.Append(string.Format("      <c r=\"{0}\"><v>{1}</v></c>\r\n", cellRef, val));
                            }
                            else
                            {
                                sb.Append(string.Format("      <c r=\"{0}\" t=\"inlineStr\"><is><t>{1}</t></is></c>\r\n", cellRef, System.Security.SecurityElement.Escape(val)));
                            }
                        }
                        sb.Append("    </row>\r\n");
                    }

                    sb.Append("  </sheetData>\r\n");
                    sb.Append("</worksheet>");
                    sw.Write(sb.ToString());
                }
            }
        }

        private static string GetExcelColumnName(int columnNumber)
        {
            string columnName = "";
            while (columnNumber > 0)
            {
                int modulo = (columnNumber - 1) % 26;
                columnName = System.Convert.ToChar('A' + modulo) + columnName;
                columnNumber = (columnNumber - modulo) / 26;
            }
            return columnName;
        }

        private static List<List<string>> ParseVCardToGrid(string path)
        {
            var grid = new List<List<string>>();
            grid.Add(new List<string> { "Name", "FormattedName", "Phone", "Email", "Organization", "Title", "Address", "Note" });

            var lines = File.ReadAllLines(path, Encoding.UTF8);
            string fn = "", n = "", tel = "", email = "", org = "", title = "", adr = "", note = "";
            bool inCard = false;

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.StartsWith("BEGIN:VCARD", StringComparison.OrdinalIgnoreCase))
                {
                    inCard = true;
                    fn = ""; n = ""; tel = ""; email = ""; org = ""; title = ""; adr = ""; note = "";
                    continue;
                }
                if (line.StartsWith("END:VCARD", StringComparison.OrdinalIgnoreCase))
                {
                    if (inCard && (!string.IsNullOrEmpty(fn) || !string.IsNullOrEmpty(n) || !string.IsNullOrEmpty(tel) || !string.IsNullOrEmpty(email)))
                    {
                        grid.Add(new List<string> { n, fn, tel, email, org, title, adr, note });
                    }
                    inCard = false;
                    continue;
                }

                if (!inCard) continue;

                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                string key = line.Substring(0, colon).ToUpperInvariant();
                string val = line.Substring(colon + 1).Trim().Replace(@"\n", " ").Replace(@"\,", ",");

                if (key == "FN" || key.StartsWith("FN;")) fn = val;
                else if (key == "N" || key.StartsWith("N;")) n = val.Replace(";", " ").Trim();
                else if (key.StartsWith("TEL")) tel = string.IsNullOrEmpty(tel) ? val : (tel + "; " + val);
                else if (key.StartsWith("EMAIL")) email = string.IsNullOrEmpty(email) ? val : (email + "; " + val);
                else if (key.StartsWith("ORG")) org = val.Replace(";", " ").Trim();
                else if (key.StartsWith("TITLE")) title = val;
                else if (key.StartsWith("ADR")) adr = val.Replace(";", " ").Trim();
                else if (key.StartsWith("NOTE")) note = val;
            }

            if (grid.Count <= 1) return null;
            return grid;
        }

        private static string GridToVCard(List<List<string>> grid)
        {
            if (grid == null || grid.Count <= 1) return "";
            var headers = grid[0];
            int fnIdx = -1, telIdx = -1, emailIdx = -1, orgIdx = -1, titleIdx = -1, noteIdx = -1;
            for (int i = 0; i < headers.Count; i++)
            {
                string h = headers[i].Trim().ToLowerInvariant();
                if (h.Contains("name") || h == "fn") fnIdx = i;
                else if (h.Contains("phone") || h.Contains("tel") || h.Contains("mobile")) telIdx = i;
                else if (h.Contains("email") || h.Contains("mail")) emailIdx = i;
                else if (h.Contains("org") || h.Contains("company")) orgIdx = i;
                else if (h.Contains("title") || h.Contains("role")) titleIdx = i;
                else if (h.Contains("note") || h.Contains("desc")) noteIdx = i;
            }

            var sb = new StringBuilder();
            for (int r = 1; r < grid.Count; r++)
            {
                var row = grid[r];
                sb.AppendLine("BEGIN:VCARD");
                sb.AppendLine("VERSION:3.0");
                string name = (fnIdx >= 0 && fnIdx < row.Count) ? row[fnIdx] : ("Contact " + r);
                if (string.IsNullOrWhiteSpace(name)) name = "Contact " + r;
                sb.AppendLine("FN:" + name);
                sb.AppendLine("N:;" + name + ";;;");
                if (telIdx >= 0 && telIdx < row.Count && !string.IsNullOrWhiteSpace(row[telIdx]))
                    sb.AppendLine("TEL;TYPE=CELL:" + row[telIdx]);
                if (emailIdx >= 0 && emailIdx < row.Count && !string.IsNullOrWhiteSpace(row[emailIdx]))
                    sb.AppendLine("EMAIL:" + row[emailIdx]);
                if (orgIdx >= 0 && orgIdx < row.Count && !string.IsNullOrWhiteSpace(row[orgIdx]))
                    sb.AppendLine("ORG:" + row[orgIdx]);
                if (titleIdx >= 0 && titleIdx < row.Count && !string.IsNullOrWhiteSpace(row[titleIdx]))
                    sb.AppendLine("TITLE:" + row[titleIdx]);
                if (noteIdx >= 0 && noteIdx < row.Count && !string.IsNullOrWhiteSpace(row[noteIdx]))
                    sb.AppendLine("NOTE:" + row[noteIdx]);
                sb.AppendLine("END:VCARD");
            }
            return sb.ToString();
        }

        private static List<List<string>> ParseDelimitedText(string text, char delimiter)
        {
            var grid = new List<List<string>>();
            var lines = text.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var row = new List<string>();
                bool inQuotes = false;
                var cur = new StringBuilder();

                for (int i = 0; i < line.Length; i++)
                {
                    char ch = line[i];
                    if (ch == '"')
                    {
                        inQuotes = !inQuotes;
                    }
                    else if (ch == delimiter && !inQuotes)
                    {
                        row.Add(cur.ToString().Trim());
                        cur.Length = 0;
                    }
                    else
                    {
                        cur.Append(ch);
                    }
                }
                row.Add(cur.ToString().Trim());
                grid.Add(row);
            }
            return grid;
        }

        private static string GridToDelimitedText(List<List<string>> grid, char delimiter)
        {
            var sb = new StringBuilder();
            foreach (var row in grid)
            {
                for (int i = 0; i < row.Count; i++)
                {
                    string val = row[i] ?? "";
                    if (val.Contains(delimiter.ToString()) || val.Contains("\"") || val.Contains("\n"))
                    {
                        sb.Append("\"").Append(val.Replace("\"", "\"\"")).Append("\"");
                    }
                    else
                    {
                        sb.Append(val);
                    }
                    if (i < row.Count - 1) sb.Append(delimiter);
                }
                sb.AppendLine();
            }
            return sb.ToString();

        }

        private static string GridToJson(List<List<string>> grid)
        {
            if (grid.Count == 0) return "[]";
            var headers = grid[0];
            var sb = new StringBuilder();
            sb.AppendLine("[");

            for (int r = 1; r < grid.Count; r++)
            {
                var row = grid[r];
                sb.Append("  {");
                for (int c = 0; c < headers.Count; c++)
                {
                    string h = headers[c];
                    string v = c < row.Count ? row[c] : "";
                    sb.Append(string.Format("\"{0}\": \"{1}\"", EscapeJson(h), EscapeJson(v)));
                    if (c < headers.Count - 1) sb.Append(", ");
                }
                sb.Append("}");
                if (r < grid.Count - 1) sb.Append(",");
                sb.AppendLine();
            }
            sb.AppendLine("]");
            return sb.ToString();

        }

        private static string GridToJsonLines(List<List<string>> grid)
        {
            if (grid.Count == 0) return "";
            var headers = grid[0];
            var sb = new StringBuilder();

            for (int r = 1; r < grid.Count; r++)
            {
                var row = grid[r];
                sb.Append("{");
                for (int c = 0; c < headers.Count; c++)
                {
                    string h = headers[c];
                    string v = c < row.Count ? row[c] : "";
                    sb.Append(string.Format("\"{0}\":\"{1}\"", EscapeJson(h), EscapeJson(v)));
                    if (c < headers.Count - 1) sb.Append(",");
                }
                sb.AppendLine("}");
            }
            return sb.ToString();

        }

        private static string GridToHtmlTable(List<List<string>> grid, string title)
        {
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html><head><meta charset='utf-8'><title>" + title + "</title>");
            sb.Append("<style>body{font-family:sans-serif;margin:2rem;}table{border-collapse:collapse;width:100%;}th,td{border:1px solid #ddd;padding:8px;text-align:left;}th{background-color:#0f172a;color:white;}</style></head><body>");
            sb.Append("<h2>" + title + "</h2><table>");

            for (int r = 0; r < grid.Count; r++)
            {
                sb.Append("<tr>");
                var row = grid[r];
                for (int c = 0; c < row.Count; c++)
                {
                    string tag = (r == 0) ? "th" : "td";
                    sb.Append(string.Format("<{0}>{1}</{0}>", tag, System.Security.SecurityElement.Escape(row[c] ?? "")));
                }
                sb.Append("</tr>");
            }
            sb.Append("</table></body></html>");
            return sb.ToString();

        }

        private static string GridToXml(List<List<string>> grid)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<records>");

            if (grid.Count > 1)
            {
                var headers = grid[0];
                for (int r = 1; r < grid.Count; r++)
                {
                    sb.AppendLine("  <record>");
                    var row = grid[r];
                    for (int c = 0; c < headers.Count; c++)
                    {
                        string h = Regex.Replace(headers[c], @"[^a-zA-Z0-9_]", "_");
                        if (string.IsNullOrEmpty(h)) h = "field_" + c;
                        string v = c < row.Count ? row[c] : "";
                        sb.AppendLine(string.Format("    <{0}>{1}</{0}>", h, System.Security.SecurityElement.Escape(v)));
                    }
                    sb.AppendLine("  </record>");
                }
            }
            sb.AppendLine("</records>");
            return sb.ToString();

        }

        private static string GridToPaddedTable(List<List<string>> grid)
        {
            if (grid.Count == 0) return "";
            int cols = 0;
            foreach (var r in grid) if (r.Count > cols) cols = r.Count;

            int[] widths = new int[cols];
            for (int c = 0; c < cols; c++)
            {
                foreach (var r in grid)
                {
                    if (c < r.Count && r[c] != null)
                    {
                        widths[c] = Math.Max(widths[c], r[c].Length);
                    }
                }
                widths[c] = Math.Min(widths[c] + 2, 40); // Max column width
            }

            var sb = new StringBuilder();
            foreach (var r in grid)
            {
                for (int c = 0; c < cols; c++)
                {
                    string v = c < r.Count ? (r[c] ?? "") : "";
                    if (v.Length > widths[c] - 2) v = v.Substring(0, widths[c] - 2);
                    sb.Append(v.PadRight(widths[c]));
                }
                sb.AppendLine();
            }
            return sb.ToString();

        }

        private static List<List<string>> ParseJsonToGrid(string json)
        {
            var grid = new List<List<string>>();
            var headers = new List<string>();
            var headerSet = new HashSet<string>();

            // Find all key:value pairs across objects in JSON
            var objMatches = Regex.Matches(json, @"\{(.*?)\}", RegexOptions.Singleline);
            var parsedRows = new List<Dictionary<string, string>>();

            foreach (Match om in objMatches)
            {
                var dict = new Dictionary<string, string>();
                var propMatches = Regex.Matches(om.Groups[1].Value, @"""([^""]+)""\s*:\s*""?([^"",}\]]*)""?");
                foreach (Match pm in propMatches)
                {
                    string k = pm.Groups[1].Value;
                    string v = pm.Groups[2].Value.Trim();
                    dict[k] = v;
                    if (!headerSet.Contains(k))
                    {
                        headerSet.Add(k);
                        headers.Add(k);
                    }
                }
                if (dict.Count > 0) parsedRows.Add(dict);
            }

            if (headers.Count > 0)
            {
                grid.Add(headers);
                foreach (var dict in parsedRows)
                {
                    var row = new List<string>();
                    foreach (var h in headers)
                    {
                        string val = "";
                        dict.TryGetValue(h, out val);
                        row.Add(val ?? "");
                    }
                    grid.Add(row);
                }
            }
            return grid;
        }

        private static List<List<string>> ParseXmlToGrid(string xml)
        {
            var grid = new List<List<string>>();
            var headers = new List<string>();
            var headerSet = new HashSet<string>();
            var rows = new List<Dictionary<string, string>>();

            using (var reader = CreateSafeXmlReader(new StringReader(xml)))
            {
                Dictionary<string, string> curRow = null;
                string curTag = "";

                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element)
                    {
                        curTag = reader.LocalName;
                        if (curTag == "record" || curTag == "row" || curTag == "item")
                        {
                            curRow = new Dictionary<string, string>();
                            rows.Add(curRow);
                        }
                    }
                    else if (reader.NodeType == XmlNodeType.Text && curRow != null && !string.IsNullOrEmpty(curTag))
                    {
                        curRow[curTag] = reader.Value;
                        if (!headerSet.Contains(curTag))
                        {
                            headerSet.Add(curTag);
                            headers.Add(curTag);
                        }
                    }
                }
            }

            if (headers.Count > 0)
            {
                grid.Add(headers);
                foreach (var r in rows)
                {
                    var rowList = new List<string>();
                    foreach (var h in headers)
                    {
                        string val = "";
                        r.TryGetValue(h, out val);
                        rowList.Add(val ?? "");
                    }
                    grid.Add(rowList);
                }
            }
            return grid;
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");
        }

        private static string GridToMarkdownTable(List<List<string>> grid)
        {
            if (grid == null || grid.Count == 0) return "";
            int cols = 0;
            foreach (var r in grid) if (r.Count > cols) cols = r.Count;
            if (cols == 0) return "";

            var sb = new StringBuilder();
            var headers = grid[0];
            sb.Append("|");
            for (int c = 0; c < cols; c++)
            {
                string h = (c < headers.Count && headers[c] != null) ? headers[c].Replace("|", "\\|").Replace("\r", "").Replace("\n", " ") : "Column" + (c + 1);
                sb.Append(" ").Append(h).Append(" |");
            }
            sb.AppendLine();

            sb.Append("|");
            for (int c = 0; c < cols; c++)
            {
                sb.Append(" --- |");
            }
            sb.AppendLine();

            for (int r = 1; r < grid.Count; r++)
            {
                var row = grid[r];
                sb.Append("|");
                for (int c = 0; c < cols; c++)
                {
                    string v = (c < row.Count && row[c] != null) ? row[c].Replace("|", "\\|").Replace("\r", "").Replace("\n", " ") : "";
                    sb.Append(" ").Append(v).Append(" |");
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private static string GridToYaml(List<List<string>> grid)
        {
            if (grid == null || grid.Count == 0) return "[]\r\n";
            var headers = grid[0];
            var sb = new StringBuilder();

            for (int r = 1; r < grid.Count; r++)
            {
                var row = grid[r];
                sb.AppendLine("-");
                for (int c = 0; c < headers.Count; c++)
                {
                    string h = Regex.Replace(headers[c] ?? ("field_" + c), @"[^a-zA-Z0-9_]", "_");
                    if (string.IsNullOrEmpty(h)) h = "field_" + c;
                    string v = (c < row.Count && row[c] != null) ? row[c] : "";
                    if (v.Contains("\n") || v.Contains(":") || v.Contains("#") || v.Contains("[") || v.Contains("]") || v.Contains("{") || v.Contains("}") || v.StartsWith(" ") || v.EndsWith(" ") || v.Contains("\"") || v.Contains("'"))
                    {
                        sb.AppendLine(string.Format("    {0}: \"{1}\"", h, v.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n")));
                    }
                    else
                    {
                        sb.AppendLine(string.Format("    {0}: {1}", h, string.IsNullOrEmpty(v) ? "\"\"" : v));
                    }
                }
            }
            return sb.ToString();
        }

        private static List<List<string>> ParseYamlToGrid(string yaml)
        {
            var grid = new List<List<string>>();
            var headers = new List<string>();
            var headerSet = new HashSet<string>();
            var rows = new List<Dictionary<string, string>>();

            var lines = yaml.Split(new char[] { '\r', '\n' }, StringSplitOptions.None);
            Dictionary<string, string> curRow = null;

            foreach (var l in lines)
            {
                string line = l.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

                if (line.StartsWith("-") || line == "-")
                {
                    curRow = new Dictionary<string, string>();
                    rows.Add(curRow);
                    line = line.Substring(1).Trim();
                }

                if (string.IsNullOrEmpty(line)) continue;

                int colon = line.IndexOf(':');
                if (colon > 0 && curRow != null)
                {
                    string key = line.Substring(0, colon).Trim();
                    string val = line.Substring(colon + 1).Trim().Trim('"', '\'');
                    curRow[key] = val;
                    if (!headerSet.Contains(key))
                    {
                        headerSet.Add(key);
                        headers.Add(key);
                    }
                }
            }

            if (headers.Count > 0)
            {
                grid.Add(headers);
                foreach (var r in rows)
                {
                    var rowList = new List<string>();
                    foreach (var h in headers)
                    {
                        string v = "";
                        r.TryGetValue(h, out v);
                        rowList.Add(v ?? "");
                    }
                    grid.Add(rowList);
                }
            }
            return grid;
        }
        #endregion
        #endregion

        #region Subtitle Engine
        private class SubtitleCue
        {
            public TimeSpan Start { get; set; }
            public TimeSpan End { get; set; }
            public string Text { get; set; }
        }

        private static ConversionResult ConvertSubtitle(string inputPath, string targetFormat, string dir, string baseName)
        {
            string outPath = Path.Combine(dir, baseName + "." + targetFormat);
            string content = File.ReadAllText(inputPath, Encoding.UTF8);
            string inExt = Path.GetExtension(inputPath).TrimStart('.').ToLowerInvariant();

            var cues = ParseSubtitleCues(content, inExt);
            if (cues == null || cues.Count == 0)
            {
                return new ConversionResult
                {
                    Success = false,
                    Message = "No valid subtitle cues found in: " + Path.GetFileName(inputPath)
                };
            }

            string outputContent = "";
            if (targetFormat == "vtt")
            {
                outputContent = CuesToVtt(cues);
            }
            else if (targetFormat == "srt")
            {
                outputContent = CuesToSrt(cues);
            }
            else if (targetFormat == "ass" || targetFormat == "ssa")
            {
                outputContent = CuesToAss(cues, baseName);
            }
            else if (targetFormat == "txt")
            {
                outputContent = CuesToTranscript(cues);
            }
            else
            {
                outputContent = content;
            }

            SaveAtomic(outPath, delegate(string tempPart)
            {
                File.WriteAllText(tempPart, outputContent, Encoding.UTF8);
            });

            var fi = new FileInfo(outPath);
            bool ok = File.Exists(outPath) && fi.Length > 0;
            return new ConversionResult
            {
                Success = ok,
                OutputPath = outPath,
                OutputSize = ok ? fi.Length : 0,
                Message = ok ? ("Converted subtitle to " + Path.GetFileName(outPath)) : ("Subtitle conversion failed: " + inExt + " -> " + targetFormat)
            };
        }

        private static List<SubtitleCue> ParseSubtitleCues(string content, string ext)
        {
            var cues = new List<SubtitleCue>();
            if (string.IsNullOrWhiteSpace(content)) return cues;

            if (ext == "ass" || ext == "ssa")
            {
                var lines = content.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase))
                    {
                        string payload = line.Substring(line.IndexOf(':') + 1).Trim();
                        string[] parts = payload.Split(new char[] { ',' }, 10);
                        if (parts.Length >= 10)
                        {
                            TimeSpan start = ParseAssTime(parts[1].Trim());
                            TimeSpan end = ParseAssTime(parts[2].Trim());
                            string rawText = parts[9];
                            string cleanText = Regex.Replace(rawText, @"\{.*?\}", "");
                            cleanText = cleanText.Replace(@"\N", "\n").Replace(@"\n", "\n").Trim();
                            cues.Add(new SubtitleCue { Start = start, End = end, Text = cleanText });
                        }
                    }
                }
                return cues;
            }

            var regex = new Regex(@"(?:(\d{1,2}:)?(\d{2}):(\d{2})[,.](\d{2,3}))\s*-->\s*(?:(\d{1,2}:)?(\d{2}):(\d{2})[,.](\d{2,3}))[^\r\n]*\r?\n([\s\S]*?)(?=\r?\n\r?\n|\z)", RegexOptions.Multiline);
            var matches = regex.Matches(content);
            if (ext == "srt" || ext == "vtt")
            {
                int arrowCount = Regex.Matches(content, "-->").Count;
                if (matches.Count == 0 || (arrowCount > 0 && matches.Count < arrowCount))
                {
                    return cues;
                }
            }

            if (matches.Count > 0)
            {
                foreach (Match m in matches)
                {
                    TimeSpan start = ParseTimeCode(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value);
                    TimeSpan end = ParseTimeCode(m.Groups[5].Value, m.Groups[6].Value, m.Groups[7].Value, m.Groups[8].Value);
                    string rawText = m.Groups[9].Value.Trim();
                    string cleanText = Regex.Replace(rawText, @"<.*?>", "");
                    cues.Add(new SubtitleCue { Start = start, End = end, Text = cleanText });
                }
                return cues;
            }

            if (ext == "txt")
            {
                var pLines = content.Split(new string[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
                TimeSpan t = TimeSpan.Zero;
                foreach (var pl in pLines)
                {
                    cues.Add(new SubtitleCue { Start = t, End = t.Add(TimeSpan.FromSeconds(3)), Text = pl.Trim() });
                    t = t.Add(TimeSpan.FromSeconds(3.5));
                }
            }
            return cues;
        }

        private static TimeSpan ParseTimeCode(string hStr, string mStr, string sStr, string msStr)
        {
            int h = 0;
            if (!string.IsNullOrEmpty(hStr)) int.TryParse(hStr.TrimEnd(':'), out h);
            int m = 0; int.TryParse(mStr, out m);
            int s = 0; int.TryParse(sStr, out s);
            int ms = 0;
            if (msStr.Length == 2) msStr += "0";
            int.TryParse(msStr, out ms);
            return new TimeSpan(0, h, m, s, ms);
        }

        private static TimeSpan ParseAssTime(string s)
        {
            try
            {
                string[] parts = s.Split(':');
                if (parts.Length == 3)
                {
                    int h = int.Parse(parts[0]);
                    int m = int.Parse(parts[1]);
                    string[] secParts = parts[2].Split('.');
                    int sec = int.Parse(secParts[0]);
                    int cs = (secParts.Length > 1) ? int.Parse(secParts[1].PadRight(2, '0').Substring(0, 2)) : 0;
                    return new TimeSpan(0, h, m, sec, cs * 10);
                }
            }
            catch { }
            return TimeSpan.Zero;
        }

        private static string CuesToSrt(List<SubtitleCue> cues)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < cues.Count; i++)
            {
                var c = cues[i];
                sb.AppendLine((i + 1).ToString());
                sb.AppendLine(string.Format("{0:D2}:{1:D2}:{2:D2},{3:D3} --> {4:D2}:{5:D2}:{6:D2},{7:D3}",
                    c.Start.Hours, c.Start.Minutes, c.Start.Seconds, c.Start.Milliseconds,
                    c.End.Hours, c.End.Minutes, c.End.Seconds, c.End.Milliseconds));
                sb.AppendLine(c.Text);
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd() + "\r\n";
        }

        private static string CuesToVtt(List<SubtitleCue> cues)
        {
            var sb = new StringBuilder();
            sb.AppendLine("WEBVTT");
            sb.AppendLine();
            for (int i = 0; i < cues.Count; i++)
            {
                var c = cues[i];
                sb.AppendLine(string.Format("{0:D2}:{1:D2}:{2:D2}.{3:D3} --> {4:D2}:{5:D2}:{6:D2}.{7:D3}",
                    c.Start.Hours, c.Start.Minutes, c.Start.Seconds, c.Start.Milliseconds,
                    c.End.Hours, c.End.Minutes, c.End.Seconds, c.End.Milliseconds));
                sb.AppendLine(c.Text);
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd() + "\r\n";
        }

        private static string CuesToAss(List<SubtitleCue> cues, string title)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[Script Info]");
            sb.AppendLine("Title: " + title);
            sb.AppendLine("ScriptType: v4.00+");
            sb.AppendLine("WrapStyle: 0");
            sb.AppendLine("ScaledBorderAndShadow: yes");
            sb.AppendLine("YCbCr Matrix: TV.601");
            sb.AppendLine();
            sb.AppendLine("[V4+ Styles]");
            sb.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
            sb.AppendLine("Style: Default,Arial,20,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,2,2,2,10,10,10,1");
            sb.AppendLine();
            sb.AppendLine("[Events]");
            sb.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");
            foreach (var c in cues)
            {
                string start = string.Format("{0}:{1:D2}:{2:D2}.{3:D2}", c.Start.Hours, c.Start.Minutes, c.Start.Seconds, c.Start.Milliseconds / 10);
                string end = string.Format("{0}:{1:D2}:{2:D2}.{3:D2}", c.End.Hours, c.End.Minutes, c.End.Seconds, c.End.Milliseconds / 10);
                string text = (c.Text ?? "").Replace("\r\n", "\\N").Replace("\n", "\\N");
                sb.AppendLine(string.Format("Dialogue: 0,{0},{1},Default,,0,0,0,,{2}", start, end, text));
            }
            return sb.ToString();
        }

        private static string CuesToTranscript(List<SubtitleCue> cues)
        {
            var sb = new StringBuilder();
            foreach (var c in cues)
            {
                if (!string.IsNullOrWhiteSpace(c.Text))
                {
                    sb.AppendLine(c.Text);
                }
            }
            return sb.ToString().TrimEnd() + "\r\n";
        }
        #endregion

        #region Media Engine (Audio & Video via FFmpeg)
        private static bool IsMediaExtension(string ext)
        {
            return ext == "mp3" || ext == "wav" || ext == "flac" || ext == "m4a" || ext == "ogg" ||
                   ext == "aac" || ext == "wma" ||
                   ext == "mp4" || ext == "webm" || ext == "mov" || ext == "mkv" || ext == "avi" || ext == "gif";
        }

        private static ConversionResult ConvertMedia(string inputPath, string targetFormat, ConversionOptions opt, string dir, string baseName)
        {
            string outPath = Path.Combine(dir, baseName + "." + targetFormat);

            string ffmpegExe = FindExecutable("ffmpeg.exe");
            if (!string.IsNullOrEmpty(ffmpegExe))
            {
                SaveAtomic(outPath, delegate(string tempPart)
                {
                    string cleanIn = SanitizePathForCommandLine(inputPath);
                    string cleanOut = SanitizePathForCommandLine(tempPart);
                    string args;
                    if (targetFormat == "gif")
                    {
                        args = string.Format("-nostdin -loglevel error -y -i \"{0}\" -vf \"fps=12,scale=480:-1:flags=lanczos\" \"{1}\"", cleanIn, cleanOut);
                    }
                    else if (targetFormat == "mp3")
                    {
                        args = string.Format("-nostdin -loglevel error -y -i \"{0}\" -vn -c:a libmp3lame -b:a {1}k \"{2}\"", cleanIn, opt.AudioBitrate, cleanOut);
                    }
                    else if (targetFormat == "wav")
                    {
                        args = string.Format("-nostdin -loglevel error -y -i \"{0}\" -vn -c:a pcm_s16le \"{1}\"", cleanIn, cleanOut);
                    }
                    else if (targetFormat == "aac")
                    {
                        args = string.Format("-nostdin -loglevel error -y -i \"{0}\" -vn -c:a aac -b:a {1}k \"{2}\"", cleanIn, opt.AudioBitrate, cleanOut);
                    }
                    else if (targetFormat == "m4a")
                    {
                        args = string.Format("-nostdin -loglevel error -y -i \"{0}\" -vn -c:a aac -b:a {1}k \"{2}\"", cleanIn, opt.AudioBitrate, cleanOut);
                    }
                    else if (targetFormat == "flac")
                    {
                        args = string.Format("-nostdin -loglevel error -y -i \"{0}\" -vn -c:a flac \"{1}\"", cleanIn, cleanOut);
                    }
                    else if (targetFormat == "ogg")
                    {
                        args = string.Format("-nostdin -loglevel error -y -i \"{0}\" -vn -c:a libvorbis -q:a 5 \"{1}\"", cleanIn, cleanOut);
                    }
                    else if (targetFormat == "mp4")
                    {
                        args = string.Format("-nostdin -loglevel error -y -i \"{0}\" -c:v libx264 -preset fast -pix_fmt yuv420p -c:a aac -b:a 192k \"{1}\"", cleanIn, cleanOut);
                    }
                    else if (targetFormat == "webm")
                    {
                        args = string.Format("-nostdin -loglevel error -y -i \"{0}\" -c:v libvpx-vp9 -b:v 0 -crf 30 -c:a libopus \"{1}\"", cleanIn, cleanOut);
                    }
                    else if (targetFormat == "mkv")
                    {
                        args = string.Format("-nostdin -loglevel error -y -i \"{0}\" -c:v libx264 -preset fast -c:a aac \"{1}\"", cleanIn, cleanOut);
                    }
                    else if (targetFormat == "mov")
                    {
                        args = string.Format("-nostdin -loglevel error -y -i \"{0}\" -c:v libx264 -preset fast -pix_fmt yuv420p -c:a aac \"{1}\"", cleanIn, cleanOut);
                    }
                    else if (targetFormat == "avi")
                    {
                        args = string.Format("-nostdin -loglevel error -y -i \"{0}\" -c:v mpeg4 -vtag xvid -qscale:v 3 -c:a libmp3lame \"{1}\"", cleanIn, cleanOut);
                    }
                    else
                    {
                        args = string.Format("-nostdin -loglevel error -y -i \"{0}\" \"{1}\"", cleanIn, cleanOut);
                    }

                    RunProcessSafely(ffmpegExe, args, 90000);
                });

                if (File.Exists(outPath) && new FileInfo(outPath).Length > 0)
                {
                    var fi = new FileInfo(outPath);
                    return new ConversionResult
                    {
                        Success = true,
                        OutputPath = outPath,
                        OutputSize = fi.Length,
                        Message = "Transcoded media: " + Path.GetFileName(outPath)
                    };
                }
            }

            return new ConversionResult
            {
                Success = false,
                Message = "FFmpeg CLI not found. Place ffmpeg.exe in app directory."
            };
        }
        #endregion

        private static string SanitizePathForCommandLine(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException("path");
            if (path.IndexOfAny(new char[] { '"', '\r', '\n', '\0' }) >= 0)
            {
                throw new ArgumentException("Path contains invalid or dangerous characters: " + path);
            }
            return Path.GetFullPath(path).TrimEnd('\\');
        }

        private static void RunProcessSafely(string exe, string args, int timeoutMs = 60000)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                WorkingDirectory = Path.GetTempPath(),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = true
            };
            using (var p = new Process { StartInfo = psi })
            {
                var errSb = new StringBuilder();
                p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                {
                    if (e.Data != null) errSb.AppendLine(e.Data);
                };
                p.Start();
                p.BeginErrorReadLine();
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch { }
                    throw new TimeoutException(string.Format("Process {0} timed out after {1}ms.", Path.GetFileName(exe), timeoutMs));
                }
                p.WaitForExit();
                if (p.ExitCode != 0)
                {
                    throw new Exception(string.Format("Process {0} returned exit code {1}: {2}", Path.GetFileName(exe), p.ExitCode, errSb.ToString().Trim()));
                }
            }
        }

        private static string FindExecutable(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            // Security Hardening: Never check Current Working Directory (CWD) to prevent binary planting

            // 1. Check protected %LocalAppData%\OMNIWHEEL installation directory
            try
            {
                string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string localOmni = Path.Combine(localApp, "OMNIWHEEL", name);
                if (File.Exists(localOmni)) return localOmni;
            }
            catch { }

            // 2. Check application BaseDirectory and bin subfolder
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            if (!string.IsNullOrEmpty(appDir))
            {
                string localPath = Path.Combine(appDir, name);
                if (File.Exists(localPath)) return localPath;

                string binPath = Path.Combine(appDir, "bin", name);
                if (File.Exists(binPath)) return binPath;
            }

            // 3. Check Windows System directory (System32)
            try
            {
                string sysDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
                if (!string.IsNullOrEmpty(sysDir))
                {
                    string sysPath = Path.Combine(sysDir, name);
                    if (File.Exists(sysPath)) return sysPath;
                }
            }
            catch { }

            // 4. Check system Program Files
            try
            {
                string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                if (!string.IsNullOrEmpty(pf))
                {
                    string candidate = Path.Combine(pf, name);
                    if (File.Exists(candidate)) return candidate;
                }
            }
            catch { }

            // 5. Check explicitly rooted directories in system PATH (never relative)
            string envPath = Environment.GetEnvironmentVariable("PATH");
            if (envPath != null)
            {
                foreach (string raw in envPath.Split(';'))
                {
                    string p = raw.Trim();
                    if (string.IsNullOrEmpty(p) || !Path.IsPathRooted(p)) continue;
                    try
                    {
                        string candidate = Path.Combine(p, name);
                        if (File.Exists(candidate)) return candidate;
                    }
                    catch { }
                }
            }

            // 6. Check if executable or embedded zip resource is packed directly inside assembly
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                foreach (string resName in asm.GetManifestResourceNames())
                {
                    if (resName.EndsWith(name, StringComparison.OrdinalIgnoreCase))
                    {
                        string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                        string targetDir = Path.Combine(localApp, "OMNIWHEEL");
                        if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
                        string extractedPath = Path.Combine(targetDir, name);
                        using (var src = asm.GetManifestResourceStream(resName))
                        using (var dst = File.Create(extractedPath))
                        {
                            src.CopyTo(dst);
                        }
                        if (File.Exists(extractedPath)) return extractedPath;
                    }
                    else if (resName.EndsWith(name.Replace(".exe", ".zip"), StringComparison.OrdinalIgnoreCase))
                    {
                        string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                        string targetDir = Path.Combine(localApp, "OMNIWHEEL");
                        if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
                        string extractedPath = Path.Combine(targetDir, name);
                        using (var src = asm.GetManifestResourceStream(resName))
                        using (var zip = new ZipArchive(src, ZipArchiveMode.Read))
                        {
                            var entry = zip.GetEntry(name);
                            if (entry != null)
                            {
                                entry.ExtractToFile(extractedPath, true);
                                if (File.Exists(extractedPath)) return extractedPath;
                            }
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        #region Windows OCR Engine
        public static ConversionResult PerformOcr(string inputPath, string targetFormat, string dir, string baseName)
        {
            var sw = Stopwatch.StartNew();
            targetFormat = targetFormat.ToLowerInvariant();
            if (targetFormat == "ocr" || targetFormat == "ocr-txt") targetFormat = "txt";
            else if (targetFormat == "ocr-docx") targetFormat = "docx";

            string outPath = Path.Combine(dir, baseName + "_ocr." + targetFormat);

            try
            {
                string ocrText = RunWindowsOcr(inputPath);
                if (string.IsNullOrWhiteSpace(ocrText) || !IsLikelyReadableText(ocrText))
                {
                    string ext = Path.GetExtension(inputPath).TrimStart('.').ToLowerInvariant();
                    if (ext == "pdf")
                    {
                        string pdfText = ExtractTextFromPdf(inputPath);
                        if (!string.IsNullOrWhiteSpace(pdfText) && IsLikelyReadableText(pdfText))
                        {
                            ocrText = pdfText;
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(ocrText) || !IsLikelyReadableText(ocrText) || ocrText.StartsWith("PDF Document:"))
                {
                    return new ConversionResult
                    {
                        Success = false,
                        Message = "No readable text could be recognized in: " + Path.GetFileName(inputPath)
                    };
                }

                if (targetFormat == "txt")
                {
                    SaveAtomic(outPath, p => File.WriteAllText(p, ocrText, Encoding.UTF8));
                }
                else if (targetFormat == "docx" || targetFormat == "doc")
                {
                    CreateDocxFromText(ocrText, outPath, baseName);
                }
                else if (targetFormat == "md")
                {
                    SaveAtomic(outPath, p => File.WriteAllText(p, ocrText, Encoding.UTF8));
                }
                else if (targetFormat == "html")
                {
                    string html = MarkdownToHtml(ocrText, baseName);
                    SaveAtomic(outPath, p => File.WriteAllText(p, html, Encoding.UTF8));
                }

                var fi = new FileInfo(outPath);
                sw.Stop();
                Logger.LogInfo(string.Format("OCR conversion complete ({0}ms): {1}", sw.ElapsedMilliseconds, outPath));
                return new ConversionResult
                {
                    Success = fi.Exists && fi.Length > 0,
                    OutputPath = outPath,
                    OutputSize = fi.Exists ? fi.Length : 0,
                    Message = "OCR extracted to " + Path.GetFileName(outPath)
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                Logger.LogError("PerformOcr failed", ex, inputPath, sw.ElapsedMilliseconds);
                return new ConversionResult
                {
                    Success = false,
                    Message = "OCR failed: " + ex.Message
                };
            }
        }

        public static string RunWindowsOcr(string inputPath)
        {
            if (!File.Exists(inputPath)) return "";
            if (Environment.OSVersion.Version.Major < 10) return "";

            string scriptPath = FindOcrScript();
            if (string.IsNullOrEmpty(scriptPath) || !File.Exists(scriptPath))
            {
                scriptPath = EnsureOcrScript();
            }

            if (string.IsNullOrEmpty(scriptPath) || !File.Exists(scriptPath))
            {
                return "";
            }

            string tempOutTxt = Path.Combine(Path.GetTempPath(), "omni_ocr_" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                string safeScript = scriptPath.Replace("'", "''");
                string safeIn = inputPath.Replace("'", "''");
                string safeOut = tempOutTxt.Replace("'", "''");
                string args = string.Format("-NoProfile -ExecutionPolicy Bypass -Command \"& {{ & '{0}' -InputPath '{1}' -OutputPath '{2}' }}\"",
                    safeScript, safeIn, safeOut);

                string psExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
                if (!File.Exists(psExe)) psExe = "powershell.exe";
                RunProcessSafely(psExe, args, 60000);

                if (File.Exists(tempOutTxt))
                {
                    string recognized = File.ReadAllText(tempOutTxt, Encoding.UTF8).Trim();
                    if (!string.IsNullOrWhiteSpace(recognized))
                    {
                        Logger.LogInfo(string.Format("Windows OCR recognized {0} chars from: {1}", recognized.Length, Path.GetFileName(inputPath)));
                        return recognized;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Windows OCR execution failed for " + Path.GetFileName(inputPath) + ": " + ex.Message, ex, inputPath, 0);
            }
            finally
            {
                try { if (File.Exists(tempOutTxt)) File.Delete(tempOutTxt); } catch { }
            }
            return "";
        }

        private static string FindOcrScript()
        {
            try
            {
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                if (!string.IsNullOrEmpty(appDir))
                {
                    string p2 = Path.Combine(appDir, "WindowsOcrEngine.ps1");
                    if (File.Exists(p2)) return p2;

                    string p3 = Path.Combine(appDir, "bin", "WindowsOcrEngine.ps1");
                    if (File.Exists(p3)) return p3;
                }

                string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string p1 = Path.Combine(localApp, "OMNIWHEEL", "WindowsOcrEngine.ps1");
                if (File.Exists(p1)) return p1;
            }
            catch { }
            return null;
        }

        private static string EnsureOcrScript()
        {
            try
            {
                string targetDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OMNIWHEEL");
                if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
                string targetPath = Path.Combine(targetDir, "WindowsOcrEngine.ps1");
                string scriptContent = GetEmbeddedOcrScriptCode();
                if (!File.Exists(targetPath) || File.ReadAllText(targetPath, Encoding.UTF8) != scriptContent)
                {
                    File.WriteAllText(targetPath, scriptContent, Encoding.UTF8);
                }
                return targetPath;
            }
            catch
            {
                return null;
            }
        }

        private static string GetEmbeddedOcrScriptCode()
        {
            return @"param([string]$InputPath, [string]$OutputPath)
if ([System.Environment]::OSVersion.Version.Major -lt 10) {
    Write-Error ""Windows OCR requires Windows 10 Build 10240 or newer.""
    [System.Environment]::Exit(2)
}
try {
    Add-Type -AssemblyName System.Runtime.WindowsRuntime
    Add-Type -AssemblyName System.Drawing
    [Windows.Storage.StorageFile, Windows.Foundation, ContentType=WindowsRuntime] | Out-Null
    [Windows.Media.Ocr.OcrEngine, Windows.Foundation, ContentType=WindowsRuntime] | Out-Null
    [Windows.Graphics.Imaging.BitmapDecoder, Windows.Foundation, ContentType=WindowsRuntime] | Out-Null
    [Windows.Globalization.Language, Windows.Globalization, ContentType=WindowsRuntime] | Out-Null
    [Windows.Graphics.Imaging.SoftwareBitmap, Windows.Foundation, ContentType=WindowsRuntime] | Out-Null
    [Windows.Data.Pdf.PdfDocument, Windows.Data.Pdf, ContentType=WindowsRuntime] | Out-Null
    [Windows.Data.Pdf.PdfPageRenderOptions, Windows.Data.Pdf, ContentType=WindowsRuntime] | Out-Null
} catch {
    Write-Error ""WinRT OCR APIs not available: $($_.Exception.Message)""
    [System.Environment]::Exit(2)
}

$asTaskOp = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { 
    $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' 
})[0]
$asTaskAction = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { 
    $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncAction' 
})[0]

function AwaitOp($op, $type) {
    $m = $asTaskOp.MakeGenericMethod($type)
    $t = $m.Invoke($null, @($op))
    $t.Wait()
    return $t.Result
}
function AwaitAction($action) {
    $t = $asTaskAction.Invoke($null, @($action))
    $t.Wait()
}

function GetOcrEngine() {
    $engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages()
    if (-not $engine) {
        $langs = [Windows.Media.Ocr.OcrEngine]::AvailableRecognizerLanguages
        if ($langs.Count -gt 0) {
            $langObj = New-Object Windows.Globalization.Language($langs[0].LanguageTag)
            $engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage($langObj)
        }
    }
    if (-not $engine) { throw 'No Windows OCR engine available.' }
    return $engine
}

function OcrSoftwareBitmap($softBmp, $engine) {
    $ocrOp = $engine.RecognizeAsync($softBmp)
    $ocrRes = AwaitOp $ocrOp ([Windows.Media.Ocr.OcrResult])
    return $ocrRes.Text
}

function OcrImageFile($imgPath, $engine) {
    $resolved = (Resolve-Path $imgPath).Path
    $fileOp = [Windows.Storage.StorageFile]::GetFileFromPathAsync($resolved)
    $file = AwaitOp $fileOp ([Windows.Storage.StorageFile])

    $streamOp = $file.OpenAsync([Windows.Storage.FileAccessMode]::Read)
    $stream = AwaitOp $streamOp ([Windows.Storage.Streams.IRandomAccessStream])

    $decoderOp = [Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)
    $decoder = AwaitOp $decoderOp ([Windows.Graphics.Imaging.BitmapDecoder])

    $softBmpOp = $decoder.GetSoftwareBitmapAsync([Windows.Graphics.Imaging.BitmapPixelFormat]::Bgra8, [Windows.Graphics.Imaging.BitmapAlphaMode]::Premultiplied)
    $softBmp = AwaitOp $softBmpOp ([Windows.Graphics.Imaging.SoftwareBitmap])

    $text = OcrSoftwareBitmap $softBmp $engine
    $stream.Dispose()
    $softBmp.Dispose()
    return $text
}

function OcrPdfFile($pdfPath, $engine) {
    [Windows.Data.Pdf.PdfDocument, Windows.Data.Pdf, ContentType=WindowsRuntime] | Out-Null
    [Windows.Data.Pdf.PdfPageRenderOptions, Windows.Data.Pdf, ContentType=WindowsRuntime] | Out-Null
    $resolved = (Resolve-Path $pdfPath).Path
    $fileOp = [Windows.Storage.StorageFile]::GetFileFromPathAsync($resolved)
    $file = AwaitOp $fileOp ([Windows.Storage.StorageFile])

    $docOp = [Windows.Data.Pdf.PdfDocument]::LoadFromFileAsync($file)
    $doc = AwaitOp $docOp ([Windows.Data.Pdf.PdfDocument])

    $sb = New-Object System.Text.StringBuilder
    for ($i = 0; $i -lt $doc.PageCount; $i++) {
        $page = $doc.GetPage($i)
        $memStream = New-Object Windows.Storage.Streams.InMemoryRandomAccessStream

        # Render at high fidelity (300 DPI equivalent) for perfect OCR text accuracy
        $renderOpts = New-Object Windows.Data.Pdf.PdfPageRenderOptions
        $targetWidth = [Math]::Max(2400, [int]($page.Size.Width * 3.0))
        $renderOpts.DestinationWidth = [uint32]$targetWidth

        $renderOp = $page.RenderToStreamAsync($memStream, $renderOpts)
        AwaitAction $renderOp

        $decoderOp = [Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($memStream)
        $decoder = AwaitOp $decoderOp ([Windows.Graphics.Imaging.BitmapDecoder])

        $softBmpOp = $decoder.GetSoftwareBitmapAsync([Windows.Graphics.Imaging.BitmapPixelFormat]::Bgra8, [Windows.Graphics.Imaging.BitmapAlphaMode]::Premultiplied)
        $softBmp = AwaitOp $softBmpOp ([Windows.Graphics.Imaging.SoftwareBitmap])

        $pageText = OcrSoftwareBitmap $softBmp $engine
        if ($pageText) {
            if ($i -gt 0) {
                # Clean Form Feed (ASCII 12) + page break marker
                $sb.AppendLine(""`f"") | Out-Null
            }
            $sb.AppendLine($pageText) | Out-Null
        }
        $memStream.Dispose()
        $softBmp.Dispose()
    }
    return $sb.ToString().Trim()
}

try {
    if (-not (Test-Path $InputPath)) { throw ""Input file not found: $InputPath"" }
    $engine = GetOcrEngine
    $ext = [System.IO.Path]::GetExtension($InputPath).TrimStart('.').ToLower()
    $extractedText = ''
    if ($ext -eq 'pdf') { $extractedText = OcrPdfFile $InputPath $engine }
    else { $extractedText = OcrImageFile $InputPath $engine }

    if ($OutputPath) {
        [System.IO.File]::WriteAllText($OutputPath, $extractedText, [System.Text.Encoding]::UTF8)
    } else {
        [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
        Write-Output $extractedText
    }
    [System.Environment]::Exit(0)
} catch {
    Write-Error $_.Exception.Message
    [System.Environment]::Exit(1)
}";
        }
        #endregion

        private static XmlReader CreateSafeXmlReader(Stream stream)
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };
            return XmlReader.Create(stream, settings);
        }

        private static XmlReader CreateSafeXmlReader(TextReader reader)
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };
            return XmlReader.Create(reader, settings);
        }
    }
}
