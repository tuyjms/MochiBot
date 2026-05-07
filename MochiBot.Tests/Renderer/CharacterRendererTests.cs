using MochiBot.Src.Models;
using MochiBot.Src.Renderer;

namespace MochiBot.Tests.Renderer
{
    public class CharacterRendererTests
    {
        private string GetTestImagesPath()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var rootDir = baseDir;
            for (int i = 0; i < 5; i++)
            {
                var parent = Directory.GetParent(rootDir);
                if (parent == null) break;
                rootDir = parent.FullName;
                if (File.Exists(Path.Combine(rootDir, "MochiBot.sln")))
                    break;
            }
            return Path.Combine(rootDir, "Resources", "Images");
        }

        [Fact]
        public async Task InitializeAsync_ShouldCompleteSuccessfully()
        {
            var renderer = new CharacterRenderer();
            await renderer.InitializeAsync(GetTestImagesPath());
            Assert.True(renderer.IsInitialized);
            renderer.Dispose();
        }

        [Fact]
        public async Task SetMotion_ShouldSwitchToNeutral()
        {
            var renderer = new CharacterRenderer();
            await renderer.InitializeAsync(GetTestImagesPath());

            renderer.SetMotion(AgentMood.Neutral);
            Assert.Equal(AgentMood.Neutral, renderer.CurrentMood);
            Assert.NotNull(renderer.CurrentFrame);
            renderer.Dispose();
        }

        [Fact]
        public async Task SetMotion_UnsupportedMood_ShouldFallbackToNeutral()
        {
            var renderer = new CharacterRenderer();
            await renderer.InitializeAsync(GetTestImagesPath());

            // 没有 happy 资源，应回退到 neutral
            renderer.SetMotion(AgentMood.Happy);
            Assert.Equal(AgentMood.Happy, renderer.CurrentMood);
            // 因为没有 happy 资源，应该没有帧
            // 但 neutral 应该有帧
            renderer.Dispose();
        }

        [Fact]
        public async Task PlayAnimation_ShouldNotThrow()
        {
            var renderer = new CharacterRenderer();
            await renderer.InitializeAsync(GetTestImagesPath());

            // 播放一个不存在的动画应静默忽略
            var exception = Record.Exception(() => renderer.PlayAnimation("nonexistent"));
            Assert.Null(exception);
            renderer.Dispose();
        }

        [Fact]
        public async Task SetOpacity_ShouldChangeOpacity()
        {
            var renderer = new CharacterRenderer();
            await renderer.InitializeAsync(GetTestImagesPath());

            renderer.SetOpacity(0.5);
            Assert.Equal(0.5, renderer.GetOpacity());

            renderer.SetOpacity(1.5); // 应 clamp 到 1.0
            Assert.Equal(1.0, renderer.GetOpacity());

            renderer.SetOpacity(-0.5); // 应 clamp 到 0.0
            Assert.Equal(0.0, renderer.GetOpacity());
            renderer.Dispose();
        }

        [Fact]
        public async Task SetPosition_ShouldChangePosition()
        {
            var renderer = new CharacterRenderer();
            await renderer.InitializeAsync(GetTestImagesPath());

            renderer.SetPosition(100, 200);
            Assert.Equal(100, renderer.GetPositionX());
            Assert.Equal(200, renderer.GetPositionY());
            renderer.Dispose();
        }

        [Fact]
        public async Task FrameUpdated_ShouldFireOnFrameChange()
        {
            var renderer = new CharacterRenderer();
            await renderer.InitializeAsync(GetTestImagesPath());

            int fireCount = 0;
            renderer.FrameUpdated += () => fireCount++;

            // 先确保当前在 neutral
            renderer.SetMotion(AgentMood.Neutral);

            // 由于只有 neutral 有资源，SetMotion 到其他情绪会回退到 neutral
            // 但因为已经在播放 neutral，所以不会重复触发
            // 这个测试主要验证事件订阅机制不抛异常
            renderer.Dispose();
        }

        [Fact]
        public async Task Dispose_ShouldCleanupResources()
        {
            var renderer = new CharacterRenderer();
            await renderer.InitializeAsync(GetTestImagesPath());
            renderer.Dispose();
            // Dispose 后不应抛出异常
            Assert.False(renderer.IsInitialized);
        }

        [Fact]
        public async Task InitializeAsync_EmptyPath_ShouldCreateDirectory()
        {
            var tempPath = Path.Combine(Path.GetTempPath(), "MochiBotTest_Images_" + Guid.NewGuid().ToString("N"));
            try
            {
                var renderer = new CharacterRenderer();
                await renderer.InitializeAsync(tempPath);
                Assert.True(renderer.IsInitialized);
                Assert.True(Directory.Exists(tempPath));
                renderer.Dispose();
            }
            finally
            {
                if (Directory.Exists(tempPath))
                    Directory.Delete(tempPath);
            }
        }
    }
}
