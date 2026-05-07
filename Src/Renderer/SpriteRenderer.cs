using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Timers;

namespace MochiBot.Src.Renderer
{
    /// <summary>
    /// 动画播放状态
    /// </summary>
    public enum AnimationState
    {
        Stopped,
        Playing,
        Paused
    }

    /// <summary>
    /// 单个精灵渲染器 - 负责渲染单帧PNG/PNG图集/GIF动画
    /// </summary>
    public class SpriteRenderer : IDisposable
    {
        private SpriteSheetLoader? _spriteLoader;
        private Image? _gifImage;
        private Image? _currentFrame;
        private System.Timers.Timer? _timer;
        private int _currentFrameIndex;
        private AnimationState _state = AnimationState.Stopped;
        private SpriteSheetConfig? _config;
        private bool _disposed;

        // GIF 相关
        private bool _isGif;
        private Guid _gifGuid;

        /// <summary>当前动画状态</summary>
        public AnimationState State => _state;

        /// <summary>当前帧索引</summary>
        public int CurrentFrameIndex => _currentFrameIndex;

        /// <summary>总帧数</summary>
        public int TotalFrames => _config?.TotalFrames ?? 1;

        /// <summary>当前帧图像</summary>
        public Image? CurrentFrame => _currentFrame;

        /// <summary>动画类型</summary>
        public string AnimationType => _config?.Type ?? "png";

        /// <summary>是否循环播放</summary>
        public bool Loop => _config?.Loop ?? true;

        /// <summary>播放完后切换到的动画名</summary>
        public string? NextAnimation => _config?.NextAnimation;

        /// <summary>帧率</summary>
        public int Fps => _config?.Fps ?? 10;

        /// <summary>帧改变事件</summary>
        public event Action<int>? FrameChanged;

        /// <summary>动画播放完毕事件（非循环动画触发）</summary>
        public event Action<string?>? AnimationCompleted;

        /// <summary>
        /// 从 JSON 配置文件加载动画
        /// </summary>
        /// <param name="jsonPath">JSON 配置文件路径</param>
        public bool LoadFromJson(string jsonPath)
        {
            if (!File.Exists(jsonPath))
                return false;

            try
            {
                var json = File.ReadAllText(jsonPath);
                var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                _config = System.Text.Json.JsonSerializer.Deserialize<SpriteSheetConfig>(json, options);
                if (_config == null) return false;

                var basePath = Path.GetDirectoryName(jsonPath) ?? ".";
                return LoadFromConfig(_config, basePath);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 从配置对象加载动画
        /// </summary>
        public bool LoadFromConfig(SpriteSheetConfig config, string basePath)
        {
            Stop();
            _config = config;

            switch (config.Type.ToLower())
            {
                case "sprite":
                    return LoadSpriteSheet(basePath);
                case "gif":
                    return LoadGif(basePath);
                case "png":
                    return LoadSinglePng(basePath);
                default:
                    return false;
            }
        }

        /// <summary>
        /// 加载精灵图（PNG图集）
        /// </summary>
        private bool LoadSpriteSheet(string basePath)
        {
            _spriteLoader?.Dispose();
            _spriteLoader = new SpriteSheetLoader();
            if (!_spriteLoader.LoadFromConfig(_config!, basePath))
                return false;

            _isGif = false;
            _currentFrameIndex = 0;
            UpdateCurrentFrame();
            return true;
        }

        /// <summary>
        /// 加载 GIF 动画
        /// </summary>
        private bool LoadGif(string basePath)
        {
            if (_config == null) return false;

            var imagePath = Path.Combine(basePath, _config.File);
            if (!File.Exists(imagePath))
                return false;

            try
            {
                _gifImage?.Dispose();
                _gifImage = Image.FromFile(imagePath);
                _isGif = true;

                // 获取 GIF 的帧维度
                var dim = new FrameDimension(_gifImage.FrameDimensionsList[0]);
                _config.TotalFrames = _gifImage.GetFrameCount(dim);
                _config.FrameWidth = _gifImage.Width;
                _config.FrameHeight = _gifImage.Height;
                _gifGuid = dim.Guid;

                _currentFrameIndex = 0;
                UpdateCurrentFrame();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 加载单帧 PNG
        /// </summary>
        private bool LoadSinglePng(string basePath)
        {
            if (_config == null) return false;

            var imagePath = Path.Combine(basePath, _config.File);
            if (!File.Exists(imagePath))
                return false;

            try
            {
                _currentFrame?.Dispose();
                _currentFrame = Image.FromFile(imagePath);
                _isGif = false;
                _config.TotalFrames = 1;
                _config.FrameWidth = _currentFrame.Width;
                _config.FrameHeight = _currentFrame.Height;
                _currentFrameIndex = 0;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 更新当前帧图像
        /// </summary>
        private void UpdateCurrentFrame()
        {
            Image? rawFrame = null;

            if (_isGif && _gifImage != null)
            {
                _gifImage.SelectActiveFrame(new FrameDimension(_gifGuid), _currentFrameIndex);
                rawFrame = new Bitmap(_gifImage);
            }
            else if (_spriteLoader != null)
            {
                rawFrame = _spriteLoader.GetFrame(_currentFrameIndex);
            }

            if (rawFrame != null)
            {
                // 直接使用原始帧，保留 PNG 透明通道
                _currentFrame?.Dispose();
                _currentFrame = rawFrame;
            }
        }

        /// <summary>
        /// 播放动画
        /// </summary>
        public void Play()
        {
            if (_config == null) return;
            if (_config.TotalFrames <= 1)
            {
                // 单帧直接显示，不需要 Timer
                _state = AnimationState.Playing;
                return;
            }

            _state = AnimationState.Playing;
            StartTimer();
        }

        /// <summary>
        /// 停止动画
        /// </summary>
        public void Stop()
        {
            _state = AnimationState.Stopped;
            StopTimer();
            _currentFrameIndex = 0;
            UpdateCurrentFrame();
        }

        /// <summary>
        /// 暂停动画
        /// </summary>
        public void Pause()
        {
            if (_state == AnimationState.Playing)
            {
                _state = AnimationState.Paused;
                StopTimer();
            }
        }

        /// <summary>
        /// 跳转到指定帧
        /// </summary>
        public void GoToFrame(int index)
        {
            if (_config == null) return;
            index = Math.Clamp(index, 0, _config.TotalFrames - 1);
            _currentFrameIndex = index;
            UpdateCurrentFrame();
            FrameChanged?.Invoke(_currentFrameIndex);
        }

        /// <summary>
        /// 启动帧切换定时器
        /// </summary>
        private void StartTimer()
        {
            StopTimer();
            if (_config == null) return;

            var interval = 1000 / _config.Fps;
            _timer = new System.Timers.Timer(Math.Max(interval, 16)) // 最小 16ms (~60fps)
            {
                AutoReset = true
            };
            _timer.Elapsed += OnTimerTick;
            _timer.Start();
        }

        /// <summary>
        /// 停止帧切换定时器
        /// </summary>
        private void StopTimer()
        {
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Elapsed -= OnTimerTick;
                _timer.Dispose();
                _timer = null;
            }
        }

        private void OnTimerTick(object? sender, ElapsedEventArgs e)
        {
            if (_config == null || _state != AnimationState.Playing)
                return;

            _currentFrameIndex++;

            if (_currentFrameIndex >= _config.TotalFrames)
            {
                if (_config.Loop)
                {
                    _currentFrameIndex = 0;
                }
                else
                {
                    // 非循环动画，播放完毕
                    _currentFrameIndex = _config.TotalFrames - 1;
                    UpdateCurrentFrame();
                    FrameChanged?.Invoke(_currentFrameIndex);
                    Stop();
                    AnimationCompleted?.Invoke(_config.NextAnimation);
                    return;
                }
            }

            UpdateCurrentFrame();
            FrameChanged?.Invoke(_currentFrameIndex);
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Stop();
            _spriteLoader?.Dispose();
            _gifImage?.Dispose();
            _currentFrame?.Dispose();
            _spriteLoader = null;
            _gifImage = null;
            _currentFrame = null;
        }
    }
}
