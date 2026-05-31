using System.IO;
using System.Drawing;
using System.Text.Json;
using static MochiBot.Src.Core.Constants;

namespace MochiBot.Src.Renderer
{
    /// <summary>
    /// 精灵图配置模型
    /// </summary>
    public class SpriteSheetConfig
    {
        public string Type { get; set; } = SpriteTypes.Sprite;   // sprite / gif / png
        public string File { get; set; } = "";
        public int FrameWidth { get; set; }
        public int FrameHeight { get; set; }
        public int TotalFrames { get; set; }
        public int Columns { get; set; } = 1;
        public int Rows { get; set; } = 1;
        public int Fps { get; set; } = 10;
        public bool Loop { get; set; } = true;
        public string? NextAnimation { get; set; }
    }

    /// <summary>
    /// 精灵图加载器 - 负责从精灵图 PNG 中裁剪帧
    /// </summary>
    public class SpriteSheetLoader : IDisposable
    {
        private Image? _spriteSheet;
        private readonly List<Rectangle> _frameRects = new();
        private SpriteSheetConfig? _config;
        private bool _disposed;

        /// <summary>当前配置</summary>
        public SpriteSheetConfig? Config => _config;

        /// <summary>总帧数</summary>
        public int FrameCount => _frameRects.Count;

        /// <summary>单帧宽度</summary>
        public int FrameWidth => _config?.FrameWidth ?? 0;

        /// <summary>单帧高度</summary>
        public int FrameHeight => _config?.FrameHeight ?? 0;

        /// <summary>
        /// 从 JSON 配置文件加载精灵图
        /// </summary>
        /// <param name="jsonPath">JSON 配置文件路径</param>
        public bool LoadFromJson(string jsonPath)
        {
            if (!File.Exists(jsonPath))
                return false;

            try
            {
                var json = File.ReadAllText(jsonPath);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                _config = JsonSerializer.Deserialize<SpriteSheetConfig>(json, options);
                if (_config == null) return false;

                return LoadFromConfig(Path.GetDirectoryName(jsonPath) ?? ".");
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 从配置对象加载精灵图
        /// </summary>
        /// <param name="config">精灵图配置</param>
        /// <param name="basePath">资源基础路径</param>
        public bool LoadFromConfig(SpriteSheetConfig config, string basePath)
        {
            _config = config;
            return LoadFromConfig(basePath);
        }

        private bool LoadFromConfig(string basePath)
        {
            if (_config == null) return false;

            // 释放旧资源
            _spriteSheet?.Dispose();
            _spriteSheet = null;
            _frameRects.Clear();

            // 加载图片
            var imagePath = Path.Combine(basePath, _config.File);
            if (!File.Exists(imagePath))
                return false;

            try
            {
                _spriteSheet = Image.FromFile(imagePath);
            }
            catch
            {
                return false;
            }

            // 计算帧矩形
            for (int row = 0; row < _config.Rows; row++)
            {
                for (int col = 0; col < _config.Columns; col++)
                {
                    if (_frameRects.Count >= _config.TotalFrames)
                        break;

                    var rect = new Rectangle(
                        col * _config.FrameWidth,
                        row * _config.FrameHeight,
                        _config.FrameWidth,
                        _config.FrameHeight
                    );
                    _frameRects.Add(rect);
                }
            }

            return _frameRects.Count > 0;
        }

        /// <summary>
        /// 获取指定索引的帧（返回裁剪后的新 Image，调用者负责释放）
        /// </summary>
        /// <param name="index">帧索引（从0开始）</param>
        public Image? GetFrame(int index)
        {
            if (_spriteSheet == null || index < 0 || index >= _frameRects.Count)
                return null;

            var rect = _frameRects[index];
            var frame = new Bitmap(rect.Width, rect.Height);
            using (var g = Graphics.FromImage(frame))
            {
                g.DrawImage(_spriteSheet,
                    new Rectangle(0, 0, rect.Width, rect.Height),
                    rect,
                    GraphicsUnit.Pixel);
            }
            return frame;
        }

        /// <summary>
        /// 获取精灵图原始 Image 对象
        /// </summary>
        public Image? GetSpriteSheet() => _spriteSheet;

        /// <summary>
        /// 获取指定索引的帧矩形
        /// </summary>
        public Rectangle GetFrameRect(int index)
        {
            if (index < 0 || index >= _frameRects.Count)
                return Rectangle.Empty;
            return _frameRects[index];
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _spriteSheet?.Dispose();
            _spriteSheet = null;
            _frameRects.Clear();
        }
    }
}
