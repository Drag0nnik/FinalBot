using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InputFiles;
using MongoDB.Driver;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Amazon.S3;
using Amazon.S3.Model;

namespace ConsoleApp2
{
    // === МОДЕЛЬ ДАННЫХ ===
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

    // === ГЛАВНЫЙ КЛАСС ===
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
            var token = Environment.GetEnvironmentVariable("BOT_TOKEN");
            long.TryParse(Environment.GetEnvironmentVariable("OWNER_ID"), out _ownerId);
            
            Console.WriteLine("--- STARTING BOT ---");

            // 1. Подключение MongoDB
            try {
                var dbClient = new MongoClient(Environment.GetEnvironmentVariable("MONGO_CONNECTION"));
                _mongo = dbClient.GetDatabase("telegram_db").GetCollection<MessageLog>("logs");
                Console.WriteLine("MongoDB: Connected");
            } catch (Exception ex) { Console.WriteLine("MongoDB Error: " + ex.Message); }

            // 2. Подключение R2
            try {
                _bucket = Environment.GetEnvironmentVariable("R2_BUCKET_NAME");
                _pubUrl = Environment.GetEnvironmentVariable("R2_PUBLIC_URL");
                var cfg = new AmazonS3Config { ServiceURL = Environment.GetEnvironmentVariable("R2_SERVICE_URL"), ForcePathStyle = true };
                _s3 = new AmazonS3Client(Environment.GetEnvironmentVariable("R2_ACCESS_KEY"), Environment.GetEnvironmentVariable("R2_SECRET_KEY"), cfg);
                Console.WriteLine("R2 Storage: Connected");
            } catch (Exception ex) { Console.WriteLine("R2 Error: " + ex.Message); }

            // 3. Запуск бота
            bot = new TelegramBotClient(token);
            using var cts = new CancellationTokenSource();
            
            await bot.ReceiveAsync(OnUpdate, OnError, new ReceiverOptions(), cts.Token);
            
            await Task.Delay(-1); // Держать программу включенной
        }

        static async Task OnUpdate(ITelegramBotClient client, Update update, CancellationToken ct)
        {
            try 
            {
                // -- НОВОЕ СООБЩЕНИЕ --
                if (update.Message is { } msg)
                {
                    string finalUrl = "";

                    // Если есть файл - грузим в R2
                    if (msg.Photo != null || msg.Video != null || msg.Voice != null || msg.Document != null)
                    {
                         finalUrl = await UploadFile(msg, ct);
                    }

                    // Сохраняем в базу
                    if (_mongo != null)
                    {
                        await _mongo.InsertOneAsync(new MessageLog {
                            ChatId = msg.Chat.Id,
                            MessageId = msg.MessageId,
                            UserId = msg.From?.Id ?? 0,
                            FirstName = msg.From?.FirstName ?? "Anonym",
                            Username = msg.From?.Username ?? "",
                            Text = msg.Text ?? msg.Caption ?? "",
                            FileUrl = finalUrl
                        });
                    }
                }

                // -- ИЗМЕНЕНИЕ СООБЩЕНИЯ --
                if (update.EditedMessage is { } edit && _ownerId != 0)
                {
                    var old = await _mongo.Find(x => x.ChatId == edit.Chat.Id && x.MessageId == edit.MessageId).FirstOrDefaultAsync();
                    
                    if (old != null)
                    {
                        string text = $"✏️ <b>ИЗМЕНЕНО</b>\n👤 {old.FirstName}\n❌ <b>Было:</b> {old.Text}\n✅ <b>Стало:</b> {edit.Text ?? edit.Caption}";
                        
                        if (!string.IsNullOrEmpty(old.FileUrl))
                        {
                            text += $"\n\n📂 <a href=\"{old.FileUrl}\">Скачать файл</a>";
                            if (old.FileUrl.EndsWith(".jpg"))
                                await client.SendPhotoAsync(_ownerId, new InputOnlineFile(old.FileUrl), text, ParseMode.Html, cancellationToken: ct);
                            else
                                await client.SendTextMessageAsync(_ownerId, text, ParseMode.Html, cancellationToken: ct);
                        }
                        else
                        {
                            await client.SendTextMessageAsync(_ownerId, text, ParseMode.Html, cancellationToken: ct);
                        }
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine("Update Error: " + ex.Message); }
        }

        static Task OnError(ITelegramBotClient c, Exception e, CancellationToken t) 
        { 
            Console.WriteLine(e.Message); return Task.CompletedTask; 
        }

        static async Task<string> UploadFile(Message msg, CancellationToken ct)
        {
            if (_s3 == null) return "";
            try {
                string fid = null, ext = ".bin";
                // Берем самое качественное фото
                if (msg.Photo != null) { fid = msg.Photo.Last().FileId; ext = ".jpg"; }
                else if (msg.Video != null) { fid = msg.Video.FileId; ext = ".mp4"; }
                else if (msg.Voice != null) { fid = msg.Voice.FileId; ext = ".ogg"; }
                else if (msg.Document != null) { fid = msg.Document.FileId; ext = Path.GetExtension(msg.Document.FileName) ?? ".doc"; }
                
                if (fid == null) return "";

                var fileInfo = await bot.GetFileAsync(fid, ct);
                using var ms = new MemoryStream();
                await bot.DownloadFileAsync(fileInfo.FilePath, ms, ct);
                ms.Position = 0;

                string name = Guid.NewGuid() + ext;
                await _s3.PutObjectAsync(new PutObjectRequest {
                    BucketName = _bucket, Key = name, InputStream = ms, DisablePayloadSigning = true
                });

                return $"{_pubUrl}/{name}";
            } catch (Exception ex) { 
                Console.WriteLine("Upload Error: " + ex.Message); 
                return ""; 
            }
        }
    }
}
