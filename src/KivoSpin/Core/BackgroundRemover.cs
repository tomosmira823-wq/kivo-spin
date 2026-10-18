using System.Net.Http;
using ImageMagick;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace KivoSpin.Core
{
    /// <summary>
    /// On-device background removal with the IS-Net general-use model (runs locally, nothing is uploaded).
    /// The model (~170 MB) is downloaded once on first use.
    /// </summary>
    public static class BackgroundRemover
    {
        private const string ModelUrl = "https://github.com/danielgatis/rembg/releases/download/v0.0.0/isnet-general-use.onnx";
        private const string ModelFile = "isnet-general-use.onnx";
        private const int Size = 1024;

        private static readonly object Gate = new object();
        private static InferenceSession _session;

        public static string ModelPath => Path.Combine(AppPaths.ModelsDir, ModelFile);
        public static bool ModelReady => File.Exists(ModelPath) && new FileInfo(ModelPath).Length > 50_000_000;

        public static string Remove(string src, Action<string> status)
        {
            using var img = Imaging.Load(src);
            Cutout(img, status);
            var outPath = AppPaths.Output(src, "png", "no bg");
            img.Write(outPath, MagickFormat.Png);
            return outPath;
        }

        /// <summary>Makes the background of img transparent, in place.</summary>
        public static void Cutout(IMagickImage<byte> img, Action<string> status)
        {
            EnsureModel(status);
            status?.Invoke("Finding the subject…");

            var session = GetSession();
            byte[] rgb;
            using (var small = img.Clone())
            {
                Imaging.FlattenOnWhite(small);
                small.ColorSpace = ColorSpace.sRGB;
                small.Resize(new MagickGeometry(Size, Size) { IgnoreAspectRatio = true });
                small.Depth = 8;
                rgb = small.ToByteArray(MagickFormat.Rgb);
            }
            if (rgb == null || rgb.Length < Size * Size * 3)
                throw new UserFacingException("Couldn't read the image pixels for background removal.");

            // IS-Net normalisation (same as rembg): x / max(x) - 0.5
            float max = 1;
            for (int i = 0; i < Size * Size * 3; i++) if (rgb[i] > max) max = rgb[i];
            int plane = Size * Size;
            var data = new float[3 * plane];
            for (int p = 0; p < plane; p++)
            {
                data[p] = rgb[p * 3] / max - 0.5f;
                data[plane + p] = rgb[p * 3 + 1] / max - 0.5f;
                data[2 * plane + p] = rgb[p * 3 + 2] / max - 0.5f;
            }

            float[] mask;
            lock (Gate)
            {
                var inputName = session.InputMetadata.Keys.First();
                var tensor = new DenseTensor<float>(data, new[] { 1, 3, Size, Size });
                using var results = session.Run(new[] { NamedOnnxValue.CreateFromTensor(inputName, tensor) });
                mask = results.First().AsTensor<float>().ToArray();
            }

            float mn = float.MaxValue, mx = float.MinValue;
            for (int i = 0; i < plane; i++) { if (mask[i] < mn) mn = mask[i]; if (mask[i] > mx) mx = mask[i]; }
            float range = Math.Max(1e-6f, mx - mn);
            var gray = new byte[plane];
            for (int i = 0; i < plane; i++)
                gray[i] = (byte)Math.Clamp((int)Math.Round((mask[i] - mn) / range * 255f), 0, 255);

            var settings = new MagickReadSettings { Width = Size, Height = Size, Format = MagickFormat.Gray };
            using var maskImg = new MagickImage(gray, settings);
            maskImg.Resize(new MagickGeometry(img.Width, img.Height) { IgnoreAspectRatio = true });

            img.Alpha(AlphaOption.Set);
            img.Composite(maskImg, CompositeOperator.CopyAlpha);
        }

        private static InferenceSession GetSession()
        {
            lock (Gate)
            {
                if (_session != null) return _session;
                try
                {
                    var opts = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
                    _session = new InferenceSession(ModelPath, opts);
                    return _session;
                }
                catch (Exception ex)
                {
                    Log.Error("Model load failed", ex);
                    AppPaths.TryDelete(ModelPath); // probably a broken download; fetch again next time
                    throw new UserFacingException("The background model didn't load. Try again and it will re-download.");
                }
            }
        }

        private static void EnsureModel(Action<string> status)
        {
            if (ModelReady) return;
            Directory.CreateDirectory(AppPaths.ModelsDir);
            var part = ModelPath + ".part";
            status?.Invoke("Downloading the background model (one time, ~170 MB)…");
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("KivoSpin/1.0");
                using var resp = http.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
                resp.EnsureSuccessStatusCode();
                long total = resp.Content.Headers.ContentLength ?? 0;
                using (var src = resp.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                using (var dst = File.Create(part))
                {
                    var buf = new byte[1 << 16];
                    long done = 0; int read; int lastPct = -1;
                    while ((read = src.Read(buf, 0, buf.Length)) > 0)
                    {
                        dst.Write(buf, 0, read);
                        done += read;
                        if (total > 0)
                        {
                            int pct = (int)(done * 100 / total);
                            if (pct != lastPct) { lastPct = pct; status?.Invoke($"Downloading the background model… {pct}%"); }
                        }
                    }
                }
                File.Move(part, ModelPath, true);
            }
            catch (Exception ex)
            {
                AppPaths.TryDelete(part);
                Log.Error("Model download failed", ex);
                throw new UserFacingException("Couldn't download the background model. Check your internet connection and try again.");
            }
        }
    }
}
