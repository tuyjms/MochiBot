using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using MochiBot.Src.Core.Config;

namespace MochiBot.Src.Services
{
    /// <summary>
    /// 截屏基础服务
    /// 提供全屏截取能力，统一在此处做截图声明总闸拦截
    /// </summary>
    public static class ScreenshotService
    {
        #region Win32 API

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int width, int height,
            IntPtr hdcSrc, int xSrc, int ySrc, uint rop);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;
        private const uint SRCCOPY = 0x00CC0020;

        #endregion

        /// <summary>
        /// 截取全屏，返回 PNG 字节数组
        /// 未声明截图权限时返回 null
        /// </summary>
        public static byte[]? CaptureScreen(IConfigReader configReader)
        {
            // 总闸：未阅读截图声明，不截图
            if (!configReader.GetModuleSettings().Vision_ScreenshotConsent)
            {
                configReader.Logger.Debug("[ScreenshotService] 截图权限未开启，跳过截屏");
                return null;
            }

            IntPtr desktopHwnd = IntPtr.Zero;
            IntPtr desktopHdc = IntPtr.Zero;
            IntPtr memoryDc = IntPtr.Zero;
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;

            try
            {
                desktopHwnd = GetDesktopWindow();
                desktopHdc = GetWindowDC(desktopHwnd);
                memoryDc = CreateCompatibleDC(desktopHdc);

                int width = GetSystemMetrics(SM_CXSCREEN);
                int height = GetSystemMetrics(SM_CYSCREEN);

                hBitmap = CreateCompatibleBitmap(desktopHdc, width, height);
                oldBitmap = SelectObject(memoryDc, hBitmap);

                BitBlt(memoryDc, 0, 0, width, height, desktopHdc, 0, 0, SRCCOPY);
                SelectObject(memoryDc, oldBitmap);

                // 通过 WPF BitmapSource 转 PNG（不依赖 System.Drawing.Common）
                var bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap, IntPtr.Zero,
                    System.Windows.Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

                using var stream = new MemoryStream();
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
                encoder.Save(stream);

                return stream.ToArray();
            }
            catch (Exception ex)
            {
                configReader.Logger.Error("[ScreenshotService] 截屏失败", ex);
                return null;
            }
            finally
            {
                if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
                if (memoryDc != IntPtr.Zero) DeleteDC(memoryDc);
                if (desktopHdc != IntPtr.Zero) ReleaseDC(desktopHwnd, desktopHdc);
            }
        }

        /// <summary>
        /// 调试用：截取全屏并保存到文件，返回文件路径
        /// 跳过权限检查，直接截取
        /// </summary>
        public static string? DebugCaptureToFile(IConfigReader configReader)
        {
            var bytes = CaptureScreenRaw(configReader);
            if (bytes == null) return null;

            var path = Path.Combine(Path.GetTempPath(), $"mochi_screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            File.WriteAllBytes(path, bytes);
            configReader.Logger.Info($"[ScreenshotService] 截图已保存: {path}");
            return path;
        }

        /// <summary>截取全屏原始 PNG 字节（跳过权限检查，供调试用）</summary>
        private static byte[]? CaptureScreenRaw(IConfigReader configReader)
        {
            IntPtr desktopHwnd = IntPtr.Zero;
            IntPtr desktopHdc = IntPtr.Zero;
            IntPtr memoryDc = IntPtr.Zero;
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;

            try
            {
                desktopHwnd = GetDesktopWindow();
                desktopHdc = GetWindowDC(desktopHwnd);
                memoryDc = CreateCompatibleDC(desktopHdc);

                int width = GetSystemMetrics(SM_CXSCREEN);
                int height = GetSystemMetrics(SM_CYSCREEN);

                hBitmap = CreateCompatibleBitmap(desktopHdc, width, height);
                oldBitmap = SelectObject(memoryDc, hBitmap);

                BitBlt(memoryDc, 0, 0, width, height, desktopHdc, 0, 0, SRCCOPY);
                SelectObject(memoryDc, oldBitmap);

                var bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap, IntPtr.Zero,
                    System.Windows.Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

                using var stream = new MemoryStream();
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
                encoder.Save(stream);

                return stream.ToArray();
            }
            catch (Exception ex)
            {
                configReader.Logger.Error("[ScreenshotService] 截屏失败", ex);
                return null;
            }
            finally
            {
                if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
                if (memoryDc != IntPtr.Zero) DeleteDC(memoryDc);
                if (desktopHdc != IntPtr.Zero) ReleaseDC(desktopHwnd, desktopHdc);
            }
        }
    }
}
