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
using Amazon.S3;
using Amazon.S3.Model;

namespace ConsoleApp2
{
    public class MessageLog
    {
        public ObjectId Id { get; set; }
        public long ChatId { get; set; }
        public int MessageId { get; set; }
        public string FirstName { get; set; }
        public string Text { get; set; }
        public string FileUrl { get; set; }
        public string FileType { get; set; }
    }

    class Program
    {
        static ITelegramBotClient bot;
        static IMongoCollection<MessageLog> _mongo;
        static AmazonS3Client _s3;
        static long _ownerId;

        static async Task Main()
        {
            StartFakeServer();
            try {
                var token = Environment.GetEnvironmentVariable("BOT_TOKEN");
                long.TryParse(Environment.GetEnvironmentVariable("OWNER_ID"), out _ownerId);
                
                // Проверка MongoDB
                var mongoUri = Environment.GetEnvironmentVariable("MONGO_CONNECTION");
                var dbClient = new MongoClient(mongoUri);
                _mongo = dbClient.GetDatabase("telegram_db").GetCollection<MessageLog>("logs");
                
                // Проверка R2
                var cfg = new AmazonS3Config { ServiceURL = Environment.GetEnvironmentVariable("R2_SERVICE_URL"), ForcePathStyle = true };
                _s3 = new AmazonS3Client(Environment.GetEnvironmentVariable("R2_ACCESS_KEY"), Environment.GetEnvironmentVariable("R2_SECRET_KEY"), cfg);

                bot = new TelegramBotClient(token);
                
                // ТЕСТОВАЯ ОТПРАВКА СЕБЕ
                await bot.SendTextMessageAsync(_ownerId, "✅ БОТ ЗАПУЩЕН И ГОТОВ ЛОГИРОВАТЬ!");
                Console.WriteLine("Связь установлена!");

                await bot.ReceiveAsync(OnUpdate, OnError, new ReceiverOptions { AllowedUpdates = Array.Empty<UpdateType>() });
            } 
            catch (Exception ex) { 
                Console.WriteLine("КРИТИЧЕСКАЯ ОШИБКА: " + ex.Message);
                await Task.Delay(5000); 
            }
            await Task.Delay(-1);
        }

        static async Task OnUpdate(ITelegramBotClient client, Update update, CancellationToken ct)
        {
            // ЛОГИРУЕМ НОВОЕ
            if (update.Message is { } msg) {
                try {
                    string fUrl = ""; string fType = "text";
                    if (msg.Photo != null || msg.Video != null || msg.Voice != null || msg.Document != null || msg.VideoNote != null)
                        (fUrl, fType) = await UploadFile(msg, ct);

                    var log = new MessageLog {
                        ChatId = msg.Chat.Id, MessageId = msg.MessageId,
                        FirstName = msg.From?.FirstName, Text = msg.Text ?? msg.Caption,
                        FileUrl = fUrl, FileType = fType
                    };
                    await _mongo.InsertOneAsync(log);
                    Console.WriteLine($"Сохранено сообщение от {log.FirstName}");
                } catch (Exception e) { Console.WriteLine("Ошибка сохранения: " + e.Message); }
            }

            // ОТЛОВ ИЗМЕНЕННЫХ
            if (update.EditedMessage is { } edit) {
                var old = await _mongo.Find(x => x.ChatId == edit.Chat.Id && x.MessageId == edit.MessageId).FirstOrDefaultAsync();
                if (old != null) {
                    string rep = $"✏️ <b>ИЗМЕНЕНО</b>\n👤 {old.FirstName}\n❌ <b>Было:</b> {old.Text ?? "[Файл]"}\n✅ <b>Стало:</b> {edit.Text ?? edit.Caption}";
                    await SendReport(client, _ownerId, rep, old.FileUrl, old.FileType, ct);
                }
            }
        }

        static async Task SendReport(ITelegramBotClient client, long admin, string text, string url, string type, CancellationToken ct) {
            try {
                if (string.IsNullOrEmpty(url)) {
                    await client.SendTextMessageAsync(admin, text, ParseMode.Html, cancellationToken: ct);
                } else {
                    var file = InputFile.FromUri(url);
                    if (type == "photo") await client.SendPhotoAsync(admin, file, caption: text, parseMode: ParseMode.Html, cancellationToken: ct);
                    else if (type == "video" || type == "videonote") await client.SendVideoAsync(admin, file, caption: text, parseMode: ParseMode.Html, cancellationToken: ct);
                    else await client.SendDocumentAsync(admin, file, caption: text, parseMode: ParseMode.Html, cancellationToken: ct);
                }
            } catch (Exception e) { Console.WriteLine("Ошибка отчета: " + e.Message); }
        }

        static async Task<(string, string)> UploadFile(Message m, CancellationToken ct) {
            try {
                string fid = m.Photo?.Last().FileId ?? m.Video?.FileId ?? m.Voice?.FileId ?? m.Document?.FileId ?? m.VideoNote?.FileId;
                string type = m.Photo != null ? "photo" : (m.Video != null ? "video" : (m.VideoNote != null ? "videonote" : "doc"));
                var file = await bot.GetFileAsync(fid, ct);
                using var ms = new MemoryStream();
                await bot.DownloadFileAsync(file.FilePath, ms, ct);
                ms.Position = 0;
                string name = Guid.NewGuid() + (type == "photo" ? ".jpg" : ".mp4");
                await _s3.PutObjectAsync(new PutObjectRequest { 
                    BucketName = Environment.GetEnvironmentVariable("R2_BUCKET_NAME"), 
                    Key = name, InputStream = ms, DisablePayloadSigning = true 
                });
                return ($"{Environment.GetEnvironmentVariable("R2_PUBLIC_URL")}/{name}", type);
            } catch { return ("", ""); }
        }

        static void StartFakeServer() {
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://*:{Environment.GetEnvironmentVariable("PORT") ?? "8080"}/");
            listener.Start();
            Task.Run(() => { while (true) { var ctx = listener.GetContext(); var b = Encoding.UTF8.GetBytes("OK"); ctx.Response.OutputStream.Write(b, 0, b.Length); ctx.Response.Close(); } });
        }
        static Task OnError(ITelegramBotClient c, Exception e, CancellationToken t) => Task.CompletedTask;
    }
}
