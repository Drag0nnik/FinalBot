using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

const string BOT_TOKEN = "PASTE_BOT_TOKEN";
const long OWNER_ID = 1244637894;

var bot = new TelegramBotClient(BOT_TOKEN);

using var cts = new CancellationTokenSource();

var receiverOptions = new ReceiverOptions
{
    AllowedUpdates = Array.Empty<UpdateType>()
};

bot.StartReceiving(
    HandleUpdateAsync,
    HandleErrorAsync,
    receiverOptions,
    cts.Token
);

Console.WriteLine("✅ Bot started");
Console.ReadLine();

async Task HandleUpdateAsync(
    ITelegramBotClient botClient,
    Update update,
    CancellationToken ct)
{
    // ===== ИЗМЕНЁННЫЕ =====
    if (update.EditedMessage != null)
    {
        var m = update.EditedMessage;

        string sender =
            m.From?.Username != null
                ? $"{m.From.FirstName} (@{m.From.Username})"
                : m.From?.FirstName ?? "Неизвестно";

        await botClient.SendTextMessageAsync(
            OWNER_ID,
            $"✏️ ИЗМЕНЕНО\nОт: {sender}\nТекст: {m.Text ?? m.Caption ?? "—"}",
            cancellationToken: ct
        );
    }

    // ===== ОБЫЧНЫЕ (чтобы бот знал, что потом удалили) =====
    if (update.Message != null)
    {
        // НИЧЕГО не шлём
        // просто факт получения
    }
}

Task HandleErrorAsync(
    ITelegramBotClient botClient,
    Exception exception,
    CancellationToken ct)
{
    Console.WriteLine(exception);
    return Task.CompletedTask;
}
