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

        /// <summary>加载历史聊天记录（带 Id，支持分页）</summary>
        public async Task<List<(int Id, ChatMessage Message)>> LoadChatHistoryWithIdAsync(int limit = 50, int offset = 0)
        {
            var result = new List<(int Id, ChatMessage Message)>();

            await using var connection = CreateConnection();
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT Id, Role, Content, Timestamp FROM chat_history ORDER BY Id DESC LIMIT @Limit OFFSET @Offset";
            cmd.Parameters.AddWithValue("@Limit", limit);
            cmd.Parameters.AddWithValue("@Offset", offset);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var id = reader.GetInt32(0);
                var msg = new ChatMessage
                {
                    Role = reader.GetString(1),
                    Content = reader.GetString(2),
                    Timestamp = DateTime.Parse(reader.GetString(3))
                };
                result.Add((id, msg));
            }

            result.Reverse();
            return result;
        }

        /// <summary>按关键词搜索聊天记录</summary>
        public async Task<List<(int Id, ChatMessage Message)>> SearchMessagesAsync(string keyword, int limit = 100)
        {
            var result = new List<(int Id, ChatMessage Message)>();

            await using var connection = CreateConnection();
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT Id, Role, Content, Timestamp FROM chat_history WHERE Content LIKE @Keyword ORDER BY Id DESC LIMIT @Limit";
            cmd.Parameters.AddWithValue("@Keyword", $"%{keyword}%");
            cmd.Parameters.AddWithValue("@Limit", limit);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var id = reader.GetInt32(0);
                var msg = new ChatMessage
                {
                    Role = reader.GetString(1),
                    Content = reader.GetString(2),
                    Timestamp = DateTime.Parse(reader.GetString(3))
                };
                result.Add((id, msg));
            }

            result.Reverse();
            return result;
        }

        /// <summary>按主键删除单条消息</summary>
        public async Task DeleteMessageByIdAsync(int id)
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM chat_history WHERE Id = @Id";
            cmd.Parameters.AddWithValue("@Id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>清空全部聊天记录</summary>
        public async Task DeleteAllMessagesAsync()
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM chat_history";
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
