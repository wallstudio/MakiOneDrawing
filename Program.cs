using System;
using System.Linq;
using SixLabors.ImageSharp;
using System.Globalization;
using System.IO;
using System.Threading;

namespace MakiOneDrawingBot
{
    static class Program
    {
        static Program() => CultureInfo.CurrentCulture = new CultureInfo("ja-JP");

        static void Main(string[] args)
        {
            var actions = new Actions(
                // https://www.slideshare.net/ngzm/oauth-10-oauth-20-openid-connect
                twitterApiKey: args.GetOption("twitter-api-key"),
                twitterApiSecret: args.GetOption("twitter-api-secret"),
                bearerToken: args.GetOption("bearer-token"),
                accessToken: args.GetOption("access-token"),
                accessTokenSecret: args.GetOption("access-token-secret"),
                googleServiceAccountJwt: args.GetOption("google-service-account-jwt"),
                date: args.GetOption("eventDate"), // nullable
                next: args.GetOption("nextDate"), // nullable
                general: args.GetOption("general")); // nullable

            // 指定時間まで待つ（タイマーの誤差を吸収する）
            if (DateTime.TryParse(args.GetOption("actionDate"), out var actionDate))
            {
                Console.WriteLine($"delay {actionDate} - {DateTime.Now}");
                var delay = actionDate - DateTime.Now;
                if (delay.TotalMinutes > 10) throw new Exception($"too long delay. {delay}");
                if (delay.TotalSeconds > 0)
                {
                    Thread.Sleep(delay);
                }
            }

            var command = args.GetOption("command");
            switch (command)
            {
                case nameof(Actions.NotificationMorning):
                    actions.NotificationMorning();
                    break;
                case nameof(Actions.NotificationStart):
                    actions.NotificationStart();
                    break;
                case nameof(Actions.NotificationFinish):
                    actions.NotificationFinish();
                    break;
                case nameof(Actions.AccumulationPosts):
                    actions.AccumulationPosts();
                    break;
                case nameof(Actions.RegeneratSummaryPage):
                    actions.RegeneratSummaryPage();
                    break;
                case "null":
                    Console.Write("Command: ");
                    var newCommand = Console.ReadLine();
                    Main(args.Select(a => a == command ? newCommand : a).ToArray());
                    break;
                case nameof(Actions.TestGenerateTextImage):
                    foreach (var (text, bin) in actions.TestGenerateTextImage())
                    {
                        Directory.CreateDirectory(nameof(Actions.TestGenerateTextImage));
                        File.WriteAllBytes($"{nameof(Actions.TestGenerateTextImage)}/{text.Replace("\n", " ")}.png", bin);
                    }
                    break;
                default:
                    throw new ArgumentException($"--command={command}");
            }
        }

        static string GetOption(this string[] args, string label)
        {
            var value = args.SkipWhile(a => a != $"--{label}").Skip(1).FirstOrDefault();
            if(value == null) throw new ArgumentException($"{label} value is null");
            if(value.StartsWith("--")) throw new ArgumentException($"{label} value is other option label? ({value})");
            return value;
        }
    }
}
