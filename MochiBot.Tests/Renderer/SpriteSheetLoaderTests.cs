using MochiBot.Src.Renderer;

namespace MochiBot.Tests.Renderer
{
    public class SpriteSheetLoaderTests
    {
        private readonly string _testJsonPath;

        public SpriteSheetLoaderTests()
        {
            // 定位到项目根目录
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            // 从测试输出目录回溯到项目根
            var rootDir = baseDir;
            for (int i = 0; i < 5; i++)
            {
                var parent = Directory.GetParent(rootDir);
                if (parent == null) break;
                rootDir = parent.FullName;
                if (File.Exists(Path.Combine(rootDir, "MochiBot.sln")))
                    break;
            }
            _testJsonPath = Path.Combine(rootDir, "Resources", "Images", "neutral", "默认", "idle.json");
        }

        [Fact]
        public void LoadFromJson_ShouldLoadSuccessfully()
        {
            var loader = new SpriteSheetLoader();
            var result = loader.LoadFromJson(_testJsonPath);
            Assert.True(result);
            Assert.NotNull(loader.Config);
            Assert.Equal("sprite", loader.Config!.Type);
            Assert.Equal(512, loader.FrameWidth);
            Assert.Equal(689, loader.FrameHeight);
            Assert.Equal(6, loader.FrameCount);
            loader.Dispose();
        }

        [Fact]
        public void GetFrame_ShouldReturnCroppedImage()
        {
            var loader = new SpriteSheetLoader();
            Assert.True(loader.LoadFromJson(_testJsonPath));

            var frame0 = loader.GetFrame(0);
            Assert.NotNull(frame0);
            Assert.Equal(512, frame0!.Width);
            Assert.Equal(689, frame0.Height);

            var frame5 = loader.GetFrame(5);
            Assert.NotNull(frame5);
            Assert.Equal(512, frame5!.Width);
            Assert.Equal(689, frame5.Height);

            frame0.Dispose();
            frame5.Dispose();
            loader.Dispose();
        }

        [Fact]
        public void GetFrame_OutOfRange_ShouldReturnNull()
        {
            var loader = new SpriteSheetLoader();
            Assert.True(loader.LoadFromJson(_testJsonPath));

            var frame = loader.GetFrame(6); // 只有6帧(0-5)
            Assert.Null(frame);

            frame = loader.GetFrame(-1);
            Assert.Null(frame);

            loader.Dispose();
        }

        [Fact]
        public void LoadFromJson_InvalidPath_ShouldReturnFalse()
        {
            var loader = new SpriteSheetLoader();
            var result = loader.LoadFromJson("nonexistent.json");
            Assert.False(result);
            loader.Dispose();
        }

        [Fact]
        public void GetFrameRect_ShouldReturnCorrectRect()
        {
            var loader = new SpriteSheetLoader();
            Assert.True(loader.LoadFromJson(_testJsonPath));

            var rect0 = loader.GetFrameRect(0);
            Assert.Equal(0, rect0.X);
            Assert.Equal(0, rect0.Y);
            Assert.Equal(512, rect0.Width);
            Assert.Equal(689, rect0.Height);

            var rect1 = loader.GetFrameRect(1);
            Assert.Equal(512, rect1.X);
            Assert.Equal(0, rect1.Y);

            var rect5 = loader.GetFrameRect(5);
            Assert.Equal(2560, rect5.X); // 5 * 512
            Assert.Equal(0, rect5.Y);

            loader.Dispose();
        }
    }
}
