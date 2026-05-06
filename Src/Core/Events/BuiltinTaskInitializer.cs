using catgirlwindow.Src.Core.Config;

namespace catgirlwindow.Src.Core.Events
{
    /// <summary>
    /// 内置任务初始化器
    /// 从配置文件读取系统内置任务配置，注册到事件调度器
    /// 可用于 feature：重启后的触发器恢复
    /// </summary>
    public class BuiltinTaskInitializer
    {
        private readonly IEventDispatcher _eventDispatcher;
        private readonly IConfigReader _configReader;

        public BuiltinTaskInitializer(IEventDispatcher eventDispatcher, IConfigReader configReader)
        {
            _eventDispatcher = eventDispatcher ?? throw new ArgumentNullException(nameof(eventDispatcher));
            _configReader = configReader ?? throw new ArgumentNullException(nameof(configReader));
        }

        /// <summary>
        /// 初始化所有内置任务
        /// 从 ModuleSettings 读取配置并注册到事件调度器
        /// </summary>
        public void Initialize()
        {
            var settings = _configReader.GetModuleSettings();

            // 1. 碎碎念 - 随机触发，表达思念或关心
            if (settings.AutoEvent_MurmurEnabled)
            {
                _eventDispatcher.RegisterTask(new CronTask
                {
                    Id = "builtin:murmur",
                    Name = "碎碎念",
                    TaskType = "murmur",
                    CronExpression = $"*/{settings.AutoEvent_MurmurInterval} * * * *",
                    Parameters = settings.AutoEvent_MurmurWeight.ToString(),
                    Enabled = true
                });
            }

            // 2. 用眼提醒 - 用户连续使用电脑超阈值
            _eventDispatcher.RegisterTask(new CronTask
            {
                Id = "builtin:eye_rest",
                Name = "用眼提醒",
                TaskType = "eye_rest",
                CronExpression = "* * * * *",
                Parameters = settings.AutoEvent_EyeRestInterval.ToString(),
                Enabled = true
            });

            // 3. 深夜关怀 - 深夜时段触发
            if (TimeSpan.TryParse(settings.AutoEvent_LateNightStart, out var lateNightStart))
            {
                _eventDispatcher.RegisterTask(new CronTask
                {
                    Id = "builtin:late_night",
                    Name = "深夜关怀",
                    TaskType = "late_night",
                    CronExpression = $"{lateNightStart.Minutes} {lateNightStart.Hours} * * *",
                    Parameters = $"{settings.AutoEvent_LateNightOffsetMin},{settings.AutoEvent_LateNightOffsetMax}",
                    Enabled = true
                });
            }

            // 4. 空闲检测 - 用户长时间未交互
            _eventDispatcher.RegisterTask(new CronTask
            {
                Id = "builtin:idle_check",
                Name = "空闲检测",
                TaskType = "idle_check",
                CronExpression = "* * * * *",
                Parameters = settings.AutoEvent_IdleThreshold.ToString(),
                Enabled = true
            });
        }

        /// <summary>
        /// 恢复已注册的任务（用于重启后恢复触发器）
        /// 从持久化存储中读取之前注册的任务并重新注册
        /// </summary>
        public void RestoreTasks(List<CronTask> savedTasks)
        {
            if (savedTasks == null) return;

            foreach (var task in savedTasks)
            {
                if (task.Id.StartsWith("builtin:")) continue;
                _eventDispatcher.RegisterTask(task);
            }
        }
    }
}
