using catgirlwindow.Src.Renderer;

namespace catgirlwindow.Tests.Renderer
{
    public class SpriteRendererTests
    {
        private string GetTestJsonPath()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var rootDir = baseDir;
            for (int i = 0; i < 5; i++)
            {
                var parent = Directory.GetParent(rootDir);
                if (parent == null) break;
                rootDir = parent.FullName;
                if (File.Exists(Path.Combine(rootDir, "catgirlwindow.sln")))
                    break;
            }
            return Path.Combine(rootDir, "Resources", "Images", "neutral", "默认", "idle.json");
        }

        [Fact]
        public void LoadFromJson_ShouldLoadSpriteSheet()
        {
            var renderer = new SpriteRenderer();
            var result = renderer.LoadFromJson(GetTestJsonPath());
            Assert.True(result);
            Assert.Equal("sprite", renderer.AnimationType);
            Assert.Equal(6, renderer.TotalFrames);
            Assert.Equal(8, renderer.Fps);
            Assert.True(renderer.Loop);
            renderer.Dispose();
        }

        [Fact]
        public void Play_ShouldStartAnimation()
        {
            var renderer = new SpriteRenderer();
            Assert.True(renderer.LoadFromJson(GetTestJsonPath()));

            Assert.Equal(AnimationState.Stopped, renderer.State);
            renderer.Play();
            Assert.Equal(AnimationState.Playing, renderer.State);
            renderer.Dispose();
        }

        [Fact]
        public void Stop_ShouldResetToFirstFrame()
        {
            var renderer = new SpriteRenderer();
            Assert.True(renderer.LoadFromJson(GetTestJsonPath()));

            renderer.Play();
            renderer.GoToFrame(3);
            Assert.Equal(3, renderer.CurrentFrameIndex);

            renderer.Stop();
            Assert.Equal(AnimationState.Stopped, renderer.State);
            Assert.Equal(0, renderer.CurrentFrameIndex);
            renderer.Dispose();
        }

        [Fact]
        public void Pause_ShouldStopTimer()
        {
            var renderer = new SpriteRenderer();
            Assert.True(renderer.LoadFromJson(GetTestJsonPath()));

            renderer.Play();
            Assert.Equal(AnimationState.Playing, renderer.State);

            renderer.Pause();
            Assert.Equal(AnimationState.Paused, renderer.State);
            renderer.Dispose();
        }

        [Fact]
        public void GoToFrame_ShouldJumpToSpecifiedFrame()
        {
            var renderer = new SpriteRenderer();
            Assert.True(renderer.LoadFromJson(GetTestJsonPath()));

            renderer.GoToFrame(2);
            Assert.Equal(2, renderer.CurrentFrameIndex);

            // 超出范围应 clamp
            renderer.GoToFrame(10);
            Assert.Equal(5, renderer.CurrentFrameIndex); // 最大 5

            renderer.GoToFrame(-1);
            Assert.Equal(0, renderer.CurrentFrameIndex); // 最小 0
            renderer.Dispose();
        }

        [Fact]
        public void CurrentFrame_ShouldNotBeNullAfterLoad()
        {
            var renderer = new SpriteRenderer();
            Assert.True(renderer.LoadFromJson(GetTestJsonPath()));

            Assert.NotNull(renderer.CurrentFrame);
            Assert.Equal(512, renderer.CurrentFrame!.Width);
            Assert.Equal(689, renderer.CurrentFrame.Height);
            renderer.Dispose();
        }

        [Fact]
        public void LoadFromJson_InvalidPath_ShouldReturnFalse()
        {
            var renderer = new SpriteRenderer();
            var result = renderer.LoadFromJson("nonexistent.json");
            Assert.False(result);
            renderer.Dispose();
        }

        [Fact]
        public void FrameChanged_ShouldFireOnFrameChange()
        {
            var renderer = new SpriteRenderer();
            Assert.True(renderer.LoadFromJson(GetTestJsonPath()));

            int firedCount = 0;
            int lastFrame = -1;
            renderer.FrameChanged += (frame) =>
            {
                firedCount++;
                lastFrame = frame;
            };

            renderer.GoToFrame(3);
            Assert.Equal(1, firedCount);
            Assert.Equal(3, lastFrame);
            renderer.Dispose();
        }

        [Fact]
        public void Dispose_ShouldCleanupResources()
        {
            var renderer = new SpriteRenderer();
            Assert.True(renderer.LoadFromJson(GetTestJsonPath()));
            renderer.Play();
            renderer.Dispose();
            // Dispose 后不应抛出异常
            Assert.Equal(AnimationState.Stopped, renderer.State);
        }
    }
}
