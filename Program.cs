using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using MongoDB.Driver;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Amazon.S3;
using Amazon.S3.Model;

namespace ConsoleApp2
{
    public class MessageLog
    {
        [BsonId] public ObjectId Id { get; set; }
        public long ChatId { get; set; }
        public int MessageId { get; set; }
        public long UserId { get; set; }
        public string FirstName { get; set; }
        public string Username { get; set; }
        public string Text { get; set; }
        public string FileUrl { get; set; } 
        public DateTime Date { get; set; } = DateTime.UtcNow;
    }

    class Program
    {
        static ITelegramBotClient bot;
        static IMongoCollection<MessageLog> _mongo;
        static AmazonS3Client _s3;
        static string _bucket;
        static string _pubUrl;
        static long _ownerId;

        static async Task Main()
        {
            // ЗАПУСКАЕМ "ФЕЙКОВЫЙ" ВЕБ-СЕРВЕР ДЛЯ RENDER
            StartFakeServer();

            var token = Environment.GetEnvironmentVariable("BOT_TOKEN");
            long.TryParse(Environment.GetEnvironmentVariable("OWNER_ID"), out _ownerId);
            
            var dbClient = new MongoClient(Environment.GetEnvironmentVariable("MONGO_CONNECTION"));
            _mongo = dbClient.GetDatabase("telegram_db").GetCollection<MessageLog>("logs");

            var cfg = new AmazonS3Config { ServiceURL = Environment.GetEnvironmentVariable("R2_SERVICE_URL"), ForcePathStyle = true };
            _s3 = new AmazonS3Client(Environment.GetEnvironmentVariable("R2_ACCESS_KEY"), Environment.GetEnvironmentVariable("R2_SECRET_KEY"), cfg);
            _bucket = Environment.GetEnvironmentVariable("R2_BUCKET_NAME");
            _pubUrl = Environment.GetEnvironmentVariable("R2_PUBLIC_URL");

            bot = new TelegramBotClient(token);
            using var cts = new CancellationTokenSource();
            await bot.ReceiveAsync(OnUpdate, OnError, new ReceiverOptions(), cts.Token);
            
            Console.WriteLine("Бот запущен и фейк-сервер работает!");
            await Task.Delay(-1);
        }

        // Эта штука заставит Render думать, что мы - сайт
        static void StartFakeServer()
        {
            var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://*:{port}/");
            listener.Start();
            Task.Run(() => {
                while (true) {
                    var context = listener.GetContext();
                    var response = context.Response;
                    string res = "Bot is alive";
                    byte[] buffer = Encoding.UTF8.GetBytes(res);
                    response.ContentLength64 = buffer.Length;
                    response.OutputStream.Write(buffer, 0, buffer.Length);
                    response.OutputStream.Close();
                }
            });
        }

        static async Task OnUpdate(ITelegramBotClient client, Update update, CancellationToken ct)
        {
            try {
                if (update.Message is { } msg) {
                    string finalUrl = "";
                    if (msg.Photo != null || msg.Video != null || msg.Voice != null || msg.Document != null)
                        finalUrl = await UploadFile(msg, ct);

                    await _mongo.InsertOneAsync(new MessageLog {
                        ChatId = msg.Chat.Id, MessageId = msg.MessageId, UserId = msg.From?.Id ?? 0,
                        FirstName = msg.From?.FirstName, Username = msg.From?.Username,
                        Text = msg.Text ?? msg.Caption, FileUrl = finalUrl
                    });
                }

                if (update.EditedMessage is { } edit && _ownerId != 0) {
                    var old = await _mongo.Find(x => x.ChatId == edit.Chat.Id && x.MessageId == edit.MessageId).FirstOrDefaultAsync();
                    if (old != null) {
                        string text = $"✏️ <b>ИЗМЕНЕНО</b>\n👤 {old.FirstName}\n❌ <b>Было:</b> {old.Text}\n✅ <b>Стало:</b> {edit.Text ?? edit.Caption}";
                        if (!string.IsNullOrEmpty(old.FileUrl)) {
                            text += $"\n\n📂 <a href=\"{old.FileUrl}\">Файл</a>";
                            if (old.FileUrl.EndsWith(".jpg"))
                                await client.SendPhotoAsync(_ownerId, InputFile.FromUri(old.FileUrl), caption: text, parseMode: ParseMode.Html, cancellationToken: ct);
                            else
                                await client.SendTextMessageAsync(_ownerId, text, ParseMode.Html, cancellationToken: ct);
                        } else await client.SendTextMessageAsync(_ownerId, text, ParseMode.Html, cancellationToken: ct);
                    }
                }
            } catch { }
        }

        static Task OnError(ITelegramBotClient c, Exception e, CancellationToken t) => Task.CompletedTask;

        static async Task<string> UploadFile(Message msg, CancellationToken ct) {
            try {
                string fid = msg.Photo?.Last().FileId ?? msg.Video?.FileId ?? msg.Voice?.FileId ?? msg.Document?.FileId;
                if (fid == null) return "";
                var file = await bot.GetFileAsync(fid, ct);
                using var ms = new MemoryStream();
                await bot.DownloadFileAsync(file.FilePath, ms, ct);
                ms.Position = 0;
                string name = Guid.NewGuid() + (msg.Photo != null ? ".jpg" : ".bin");
                await _s3.PutObjectAsync(new PutObjectRequest { BucketName = _bucket, Key = name, InputStream = ms, DisablePayloadSigning = true });
                return $"{_pubUrl}/{name}";
            } catch { return ""; }
        }
    }
}
