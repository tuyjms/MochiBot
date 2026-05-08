using System.Drawing;
using System.IO;
using MochiBot.Src.EventModels;

namespace MochiBot.Src.Renderer
{
    /// <summary>
    /// 角色动画渲染器状态机 - 实现 ICharacterRenderer 接口
    /// 负责管理情绪→动画映射，指挥 SpriteRenderer 渲染
    /// </summary>
    public class CharacterRenderer : ICharacterRenderer, IDisposable
    {
        // 情绪→动作目录映射
        private readonly Dictionary<AgentMood, List<string>> _moodAnimations = new();
        // 动作名→SpriteRenderer 缓存
        private readonly Dictionary<string, SpriteRenderer> _rendererCache = new();
        // 资源根路径
        private string _resourcePath = "";
        // 当前活跃的 SpriteRenderer
        private SpriteRenderer? _currentRenderer;
        // 当前情绪
        private AgentMood _currentMood = AgentMood.Neutral;
        // 当前动作名
        private string? _currentAnimationName;
        // 透明度
        private double _opacity = 1.0;
        // 位置
        private int _posX, _posY;
        private bool _disposed;
        private bool _initialized;

        /// <summary>当前情绪</summary>
        public AgentMood CurrentMood => _currentMood;

        /// <summary>当前动作名</summary>
        public string? CurrentAnimationName => _currentAnimationName;

        /// <summary>当前帧 PNG 字节数据（线程安全）</summary>
        public byte[]? CurrentFrame => _currentRenderer?.CurrentFrameBytes;

        /// <summary>是否已初始化</summary>
        public bool IsInitialized => _initialized;

        /// <summary>帧改变事件（供 UI 层订阅以刷新 PictureBox）</summary>
        public event Action? FrameUpdated;

        /// <summary>
        /// 初始化渲染器
        /// </summary>
        /// <param name="resourcePath">角色资源文件夹路径（如 Resources/Images/）</param>
        public Task InitializeAsync(string resourcePath)
        {
            _resourcePath = resourcePath;
            _moodAnimations.Clear();
            _rendererCache.Clear();

            // 扫描资源目录，建立情绪→动作目录映射
            if (!Directory.Exists(resourcePath))
            {
                Directory.CreateDirectory(resourcePath);
            }

            ScanMoodDirectories();

            _initialized = true;

            // 默认加载 Neutral 情绪
            SetMotion(AgentMood.Neutral);

            return Task.CompletedTask;
        }

        /// <summary>
        /// 扫描资源目录，建立情绪→动作目录映射
        /// 目录结构: Resources/Images/{情绪名}/{动作名}/
        /// </summary>
        private void ScanMoodDirectories()
        {
            foreach (var moodDir in Directory.GetDirectories(_resourcePath))
            {
                var dirName = Path.GetFileName(moodDir);
                var mood = ParseMood(dirName);
                if (mood == null) continue;

                var actions = new List<string>();
                foreach (var actionDir in Directory.GetDirectories(moodDir))
                {
                    var actionName = Path.GetFileName(actionDir);
                    actions.Add(actionName);
                }

                if (actions.Count > 0)
                {
                    _moodAnimations[mood.Value] = actions;
                }
            }
        }

        /// <summary>
        /// 将目录名解析为 AgentMood
        /// </summary>
        private static AgentMood? ParseMood(string dirName)
        {
            return dirName.ToLowerInvariant() switch
            {
                "happy" => AgentMood.Happy,
                "sad" => AgentMood.Sad,
                "sleepy" => AgentMood.Sleepy,
                "touched" => AgentMood.Touched,
                "neutral" => AgentMood.Neutral,
                "teasing" => AgentMood.Teasing,
                "angry" => AgentMood.Angry,
                "surprised" => AgentMood.Surprised,
                _ => null
            };
        }

        /// <summary>
        /// 获取情绪对应的动作目录路径
        /// </summary>
        private string GetMoodDir(AgentMood mood)
        {
            var moodName = mood.ToString().ToLowerInvariant();
            return Path.Combine(_resourcePath, moodName);
        }

        /// <summary>
        /// 根据情绪切换动画
        /// </summary>
        public void SetMotion(AgentMood mood)
        {
            if (!_initialized) return;
            _currentMood = mood;

            // 获取该情绪下的可用动作列表
            if (!_moodAnimations.TryGetValue(mood, out var actions) || actions.Count == 0)
            {
                // 没有对应情绪的资源，尝试回退到 neutral
                if (mood != AgentMood.Neutral)
                {
                    SetMotion(AgentMood.Neutral);
                }
                return;
            }

            // 默认选择第一个动作
            var actionName = actions[0];
            PlayAnimationInternal(actionName);
        }

        /// <summary>
        /// 播放指定动画
        /// </summary>
        public void PlayAnimation(string animationName)
        {
            if (!_initialized) return;
            PlayAnimationInternal(animationName);
        }

        /// <summary>
        /// 内部播放动画逻辑
        /// </summary>
        private void PlayAnimationInternal(string actionName)
        {
            if (string.IsNullOrEmpty(actionName)) return;

            // 查找动作目录
            var moodDir = GetMoodDir(_currentMood);
            var actionDir = Path.Combine(moodDir, actionName);
            if (!Directory.Exists(actionDir))
            {
                // 在所有情绪目录中搜索
                foreach (var kvp in _moodAnimations)
                {
                    if (kvp.Value.Contains(actionName))
                    {
                        actionDir = Path.Combine(GetMoodDir(kvp.Key), actionName);
                        break;
                    }
                }
                if (!Directory.Exists(actionDir)) return;
            }

            // 如果已有相同动画在播放，不重复加载
            if (_currentAnimationName == actionName && _currentRenderer != null)
                return;

            // 停止当前动画
            _currentRenderer?.Stop();

            // 从缓存获取或创建 SpriteRenderer
            if (!_rendererCache.TryGetValue(actionName, out var renderer))
            {
                renderer = new SpriteRenderer();
                var jsonFile = FindJsonConfig(actionDir);
                if (jsonFile != null)
                {
                    if (!renderer.LoadFromJson(jsonFile))
                    {
                        renderer.Dispose();
                        return;
                    }
                }
                else
                {
                    // 没有 JSON 配置，尝试直接加载目录中的图片
                    var pngFile = Directory.GetFiles(actionDir, "*.png").FirstOrDefault();
                    if (pngFile == null) return;

                    var config = new SpriteSheetConfig
                    {
                        Type = "png",
                        File = Path.GetFileName(pngFile)
                    };
                    if (!renderer.LoadFromConfig(config, actionDir))
                    {
                        renderer.Dispose();
                        return;
                    }
                }

                // 订阅动画完成事件
                renderer.AnimationCompleted += OnAnimationCompleted;
                renderer.FrameChanged += OnFrameChanged;
                _rendererCache[actionName] = renderer;
            }

            _currentRenderer = renderer;
            _currentAnimationName = actionName;
            _currentRenderer.Play();

            // 触发帧更新
            FrameUpdated?.Invoke();
        }

        /// <summary>
        /// 在动作目录中查找 JSON 配置文件
        /// </summary>
        private static string? FindJsonConfig(string dirPath)
        {
            var jsonFiles = Directory.GetFiles(dirPath, "*.json");
            if (jsonFiles.Length == 0) return null;

            // 优先选择与目录同名的 JSON 文件
            var dirName = Path.GetFileName(dirPath);
            var preferred = jsonFiles.FirstOrDefault(f =>
                Path.GetFileNameWithoutExtension(f).Equals(dirName, StringComparison.OrdinalIgnoreCase));
            if (preferred != null) return preferred;

            return jsonFiles[0];
        }

        /// <summary>
        /// 动画播放完毕回调
        /// </summary>
        private void OnAnimationCompleted(string? nextAnimation)
        {
            if (!string.IsNullOrEmpty(nextAnimation))
            {
                // 切换到下一个动画
                PlayAnimationInternal(nextAnimation);
            }
        }

        /// <summary>
        /// 帧改变回调
        /// </summary>
        private void OnFrameChanged(int frameIndex)
        {
            FrameUpdated?.Invoke();
        }

        /// <summary>
        /// 设置角色透明度
        /// </summary>
        public void SetOpacity(double opacity)
        {
            _opacity = Math.Clamp(opacity, 0.0, 1.0);
        }

        /// <summary>
        /// 获取当前透明度
        /// </summary>
        public double GetOpacity() => _opacity;

        /// <summary>
        /// 设置角色在窗口中的位置
        /// </summary>
        public void SetPosition(int x, int y)
        {
            _posX = x;
            _posY = y;
        }

        /// <summary>
        /// 获取当前位置 X
        /// </summary>
        public int GetPositionX() => _posX;

        /// <summary>
        /// 获取当前位置 Y
        /// </summary>
        public int GetPositionY() => _posY;

        /// <summary>
        /// 释放渲染资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var renderer in _rendererCache.Values)
            {
                renderer.AnimationCompleted -= OnAnimationCompleted;
                renderer.FrameChanged -= OnFrameChanged;
                renderer.Dispose();
            }
            _rendererCache.Clear();
            _moodAnimations.Clear();
            _currentRenderer = null;
            _initialized = false;
        }
    }
}
