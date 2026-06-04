using Microsoft.Data.Sqlite;
using MochiBot.Src.Core.Database;
using MochiBot.Src.EventModels;

namespace MochiBot.Src.Services
{
    /// <summary>
    /// 聊天记录数据访问中间层
    /// 负责聊天记录表的所有 SQL 操作
    /// </summary>
    public class ChatHistoryRepository
    {
        private readonly IDatabaseService _databaseService;

        public ChatHistoryRepository(IDatabaseService databaseService)
        {
            _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
        }

        private SqliteConnection CreateConnection()
        {
            var connectionString = _databaseService.GetConnectionString();
            return new SqliteConnection(connectionString.Contains("Pooling=")
                ? connectionString
                : $"{connectionString};Pooling=False");
        }

        /// <summary>追加单条消息到聊天历史（增量写入，不清空表）</summary>
        public async Task SaveSingleMessageAsync(ChatMessage message)
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT INTO chat_history (Role, Content, Timestamp) VALUES (@Role, @Content, @Timestamp)";
            cmd.Parameters.AddWithValue("@Role", message.Role);
            cmd.Parameters.AddWithValue("@Content", message.Content);
            cmd.Parameters.AddWithValue("@Timestamp", message.Timestamp.ToString("O"));
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>保存聊天记录到历史</summary>
        public async Task SaveChatHistoryAsync(List<ChatMessage> messages)
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync();

            // 清空旧数据后批量插入
            using var clearCmd = connection.CreateCommand();
            clearCmd.CommandText = "DELETE FROM chat_history";
            await clearCmd.ExecuteNonQueryAsync();

            foreach (var msg in messages)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "INSERT INTO chat_history (Role, Content, Timestamp) VALUES (@Role, @Content, @Timestamp)";
                cmd.Parameters.AddWithValue("@Role", msg.Role);
                cmd.Parameters.AddWithValue("@Content", msg.Content);
                cmd.Parameters.AddWithValue("@Timestamp", msg.Timestamp.ToString("O"));
                await cmd.ExecuteNonQueryAsync();
            }
        }

        /// <summary>加载历史聊天记录</summary>
        public async Task<List<ChatMessage>> LoadChatHistoryAsync(int limit = 50)
        {
            var result = new List<ChatMessage>();

            await using var connection = CreateConnection();
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT Role, Content, Timestamp FROM chat_history ORDER BY Id DESC LIMIT @Limit";
            cmd.Parameters.AddWithValue("@Limit", limit);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new ChatMessage
                {
                    Role = reader.GetString(0),
                    Content = reader.GetString(1),
                    Timestamp = DateTime.Parse(reader.GetString(2))
                });
            }

            // 反转回正序
            result.Reverse();
            return result;
        }
    }
}
