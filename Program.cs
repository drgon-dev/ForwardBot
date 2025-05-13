using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using Telegram.Bot.Requests;
using System.Text;
using Microsoft.Extensions.Configuration;
using System;
using System.Linq;
using MySql.Data.MySqlClient;
using System.IO;
using Newtonsoft.Json;
using System.Diagnostics;

public class TelegramBotConfig
{
    public string BotToken { get; set; }
    public bool debug {  get; set; }
    public string main_channel { get; set; }
    public string hub_channel { get; set; }
    public static TelegramBotConfig GetConfiguration()
    {
        string filePath = "appsettings.json";

        if (!System.IO.File.Exists(filePath))
        {
            // Создание настроек по умолчанию
            TelegramBotConfig defaultConfig = new TelegramBotConfig
            {
                BotToken = "YOUR_BOT_TOKEN_HERE",
                debug = false,
                main_channel = "@CHANNEL OR CHANNEL ID",
                hub_channel = "@CHANNEL"
            };

            // Инициализация класса подключения к mysql
            SqlAdapterConfig sql = new SqlAdapterConfig
            {
                minimumHours = 24,
                arg = "server=_;user=_;password=_;database=_;"
            };

            // Формирование структуры TelegramBotConfig в JSON формате
            string defaultConfigJson = JsonConvert.SerializeObject(new { TelegramBotConfig = defaultConfig, SqlAdapterConfig = sql}, Formatting.Indented);
            System.IO.File.WriteAllText(filePath, defaultConfigJson);

            // Возвращаем настройки по умолчанию
            return defaultConfig;
        }
        // Создание json файла
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json");

        IConfigurationRoot configuration = builder.Build();

        var telegramBotConfig = new TelegramBotConfig();
        var telegramBotConfigSection = configuration.GetSection("TelegramBotConfig");
        telegramBotConfig.BotToken = telegramBotConfigSection["BotToken"];
        telegramBotConfig.debug = Convert.ToBoolean(telegramBotConfigSection["debug"]);
        telegramBotConfig.main_channel = telegramBotConfigSection["main_channel"];
        telegramBotConfig.hub_channel = telegramBotConfigSection["hub_channel"];

        return telegramBotConfig;
    }
}


class Program
{
    // Клиент для работы с Telegram Bot API
    private static ITelegramBotClient _botClient;

    // Объект с настройками работы бота
    private static ReceiverOptions _receiverOptions;

    static async Task Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        var telegramBotConfig = TelegramBotConfig.GetConfiguration();
        
        var token = telegramBotConfig.BotToken;

        // Присваивание значения токена 
        _botClient = new TelegramBotClient(token);
        Console.WriteLine($"Bot Token: {telegramBotConfig.BotToken}");

        // Присваивание значений настроек бота
        _receiverOptions = new ReceiverOptions
        {
            // Типы получаемых Update
            AllowedUpdates = new[]
            {
                UpdateType.CallbackQuery,
                UpdateType.Message,
            },

            //ThrowPendingUpdates = true,
            DropPendingUpdates = true,
        };
        bool debug = TelegramBotConfig.GetConfiguration().debug;
        if (debug)
        {
            Console.WriteLine("Debug enabled!");
        }
        else
        {
            Console.WriteLine("Debug disabled!");
        }
        using var cts = new CancellationTokenSource();

        // UpdateHander - обработчик приходящих Update
        // ErrorHandler - обработчик ошибок, связанных с Bot API
        _botClient.StartReceiving(UpdateHandler, ErrorHandler, _receiverOptions, cts.Token);

        // Переменная, в которой хранится информация о боте.
        var me = await _botClient.GetMe();
        Console.WriteLine($"{me.FirstName} запущен!");

        await Task.Delay(-1); // Постоянная задержка, чтобы бот работал постоянно
    }
    private static async Task UpdateHandler(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        try
        {
            bool debug = TelegramBotConfig.GetConfiguration().debug;
            switch (update.Type)
            {
                case UpdateType.CallbackQuery:
                    {
                        var callback = update.CallbackQuery;
                        var user = callback.From;
                        var hub = await botClient.GetChat(TelegramBotConfig.GetConfiguration().hub_channel);
                        var chatMember = await botClient.GetChatMember(TelegramBotConfig.GetConfiguration().hub_channel, user.Id);
                        var mainChannelMember = await botClient.GetChatMember(TelegramBotConfig.GetConfiguration().main_channel, user.Id);

                        if (callback.Data == "check")
                        {
                            if (chatMember.Status == ChatMemberStatus.Member || chatMember.Status == ChatMemberStatus.Administrator || chatMember.Status == ChatMemberStatus.Creator)
                            {
                                if (mainChannelMember.Status == ChatMemberStatus.Member || mainChannelMember.Status == ChatMemberStatus.Administrator || mainChannelMember.Status == ChatMemberStatus.Creator)
                                {
                                    await botClient.SendMessage(
                                    user.Id,
                                    "Добро пожаловать!\n" +
                                    "Просто отправть мне сообщение, а я сделаю анонимный пост\n");
                                    await botClient.DeleteMessage(user.Id, callback.Message.MessageId);
                                    return;
                                }
                                else
                                {
                                    var inLineKeyboard = new InlineKeyboardMarkup(
                                        new List<InlineKeyboardButton[]>() // лист c кнопками
                                        {

                                            new InlineKeyboardButton[]
                                            {
                                                InlineKeyboardButton.WithUrl(hub.Title, hub.InviteLink),
                                            }
                                        }
                                    );
                                    await botClient.SendMessage(
                                    user.Id,
                                    $"Далее необходимо в канале {hub.Title} прочитать правила использования и подать заявку на вступление в основной канал",
                                    replyMarkup: inLineKeyboard); // Все клавиатуры передаются в параметр replyMarkup
                                    return;
                                }
                            }
                            else
                            {
                                await botClient.AnswerCallbackQuery(callback.Id, "Вы не подписаны на канал", true);
                                return;
                            }
                        }
                        break;
                    }
                case UpdateType.Message:
                {
                    // Инициализация класса для работы с SQL
                    SqlAdapter sql = new SqlAdapter();
                    var message = update.Message;
                    var user = message.From;
                    var chat = message.Chat;
                    var hub = await botClient.GetChat(TelegramBotConfig.GetConfiguration().hub_channel);
                    var chatMember = await botClient.GetChatMember(TelegramBotConfig.GetConfiguration().hub_channel, user.Id);
                    var mainChannelMember = await botClient.GetChatMember(TelegramBotConfig.GetConfiguration().main_channel, user.Id);

                    if (chatMember.Status == ChatMemberStatus.Member || chatMember.Status == ChatMemberStatus.Administrator || chatMember.Status == ChatMemberStatus.Creator)
                    { 
                        if (message.Text == "ХУЙ" && message.Type == MessageType.Text)
                        {
                            await botClient.ApproveChatJoinRequest(TelegramBotConfig.GetConfiguration().main_channel, user.Id);
                        }
                        if (mainChannelMember.Status == ChatMemberStatus.Member || mainChannelMember.Status != ChatMemberStatus.Administrator || mainChannelMember.Status == ChatMemberStatus.Creator)
                        {
                            if (message.Text == "/start" && message.Type == MessageType.Text)
                            {
                                await botClient.SendMessage(
                                chat.Id,
                                "Добро пожаловать!\n" +
                                "Просто отправь мне сообщение, а я сделаю анонимный пост\n");
                                return;
                            }
                            else
                            {
                                long userId = user.Id;

                                if (sql.CheckLog(userId))
                                {
                                    if (sql.CheckElapsedTime(userId, message.Date) == 0)
                                    {
                                        await botClient.SendMessage(chat.Id, $"Ещё не прошло {sql.minimumHours} часа с последнего сообщения, это сообщение не отправится!ПОШЁЛ НАХУЙ");
                                        break;
                                    }
                                    sql.DeleteLog(sql.Find(userId));
                                }
                                sql.AddLog(userId, message.Date);

                                await botClient.CopyMessage(TelegramBotConfig.GetConfiguration().main_channel, chat.Id, message.MessageId);

                                if (debug)
                                {
                                    Console.WriteLine($"Пользователь {user.FirstName} (ID: {user.Id}) подписан на канал.");
                                    Console.WriteLine(message.Date.ToString());
                                    Console.WriteLine($"Имя:{user.FirstName}, ID:{user.Id} написал соообщение:{message.Text}");
                                }
                                return;
                            }
                        }
                        else
                        {
                            var inLineKeyboard = new InlineKeyboardMarkup(
                                new List<InlineKeyboardButton[]>() 
                                {
                                    new InlineKeyboardButton[] 
                                    {
                                        InlineKeyboardButton.WithUrl(hub.Title, hub.InviteLink),
                                    }
                                }
                            );
                            await botClient.SendMessage(
                            chat.Id,
                            $"Далее необходимо в канале {hub.Title} прочитать правила использования и подать заявку на вступление в основной канал",
                            replyMarkup: inLineKeyboard);
                            return;
                        }
                    }
                    else
                    {
                        if(debug)
                            Console.WriteLine($"Пользователь {user.FirstName} (ID: {user.Id} не подписан на канал.");
                        var inLineKeyboard = new InlineKeyboardMarkup(
                            new List<InlineKeyboardButton[]>()
                            {
                                new InlineKeyboardButton[]
                                {
                                    InlineKeyboardButton.WithUrl(hub.Title, hub.InviteLink),
                                },
                                new InlineKeyboardButton[]
                                {
                                    InlineKeyboardButton.WithCallbackData("Проверить", "check"),
                                },
                            }
                        );
                        await botClient.SendMessage(
                        chat.Id,
                        "Чтобы продолжить пользоваться ботом необходимо подписаться на канал",
                        replyMarkup: inLineKeyboard); 
                        return;
                    }
                }
                
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }

    private static Task ErrorHandler(ITelegramBotClient botClient, Exception error, CancellationToken cancellationToken)
    {
        var ErrorMessage = error switch
        {
            ApiRequestException apiRequestException
                => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
            _ => error.ToString()
        };

        Console.WriteLine(ErrorMessage);
        return Task.CompletedTask;
    }
}
