using System;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

class Program
{
    static async Task Main()
    {
        var botToken = Environment.GetEnvironmentVariable("BOT_TOKEN");
        if (string.IsNullOrEmpty(botToken))
        {
            Console.WriteLine("BOT_TOKEN is not set");
            return;
        }

        var bot = new TelegramBotClient(botToken);

        var me = await bot.GetMeAsync();
        Console.WriteLine($"Bot started: @{me.Username}");

        using var cts = new CancellationTokenSource();

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = Array.Empty<UpdateType>()
        };

        bot.StartReceiving(
            HandleUpdateAsync,
            HandleErrorAsync,
            receiverOptions,
            cancellationToken: cts.Token
        );

        Console.ReadLine();
        cts.Cancel();
    }

    static async Task HandleUpdateAsync(
        ITelegramBotClient bot,
        Update update,
        CancellationToken cancellationToken)
    {
        if (update.Type == UpdateType.EditedMessage)
        {
            var msg = update.EditedMessage;
            if (msg == null) return;

            string text =
                "✏️ ИЗМЕНЁННОЕ СООБЩЕНИЕ\n\n" +
                $"👤 Chat ID: {msg.Chat.Id}\n" +
                $"🆔 Message ID: {msg.MessageId}\n" +
                $"📝 Новый текст:\n{msg.Text}";

            await bot.SendTextMessageAsync(
                chatId: 1244637894,
                text: text,
                cancellationToken: cancellationToken
            );
        }
    }

    static Task HandleErrorAsync(
        ITelegramBotClient bot,
        Exception exception,
        CancellationToken cancellationToken)
    {
        Console.WriteLine(exception);
        return Task.CompletedTask;
    }
}
