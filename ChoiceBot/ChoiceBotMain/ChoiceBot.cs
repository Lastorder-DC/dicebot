﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ChoiceBot.BotCommon;
using Nadulmok.SocialApi;

namespace ChoiceBot.ChoiceBotMain
{
    public class ChoiceBot: BotBase
    {
        private readonly Random _rand = new Random();

        private const string HelpText = 
            "- 주사위: (주사위 개수)d(주사위 숫자) 를 보내주세요 (예: d5 2d10 등)\r\n"
            + "- 도움말: 이 내용을 보내드려요";

        public ChoiceBot(IApiClient client) : base(client)
        {
        }
        
        public override IEnumerable<StatusProcessor> BuildPipeline()
        {
            var list = new List<StatusProcessor>()
            {
                PipeHelp,
                PipeDice,
                PipeNotHandledHelp
            };
            
            return base.BuildPipeline().Concat(list);
        }

        private async Task PipeHelp(INote status, Func<Task> next)
        {
            if (!status.Content.Contains("도움말") && !status.Content.Contains("사용법") && !status.Content.Contains("도와줘"))
            {
                await next();
                return;
            }

            await ReplyTo(status, HelpText);
        }

        private List<YesNoInfo>? pipeYesNoRegex = null; // TODO: performance improvement
        
        private class YesNoInfo
        {
            public string Lang { get; set; } = null!;
            public Regex Regex { get; set; } = null!;
            public string Yes { get; set; } = null!;
            public string No { get; set; } = null!;
        }
        
        private async Task PipeYesNo(INote status, Func<Task> next)
        {
            if (pipeYesNoRegex == null)
            {
                pipeYesNoRegex = new List<YesNoInfo>()
                {
                    new YesNoInfo
                    {
                        Regex = new Regex("([예네](아니[요오]|아뇨|니[요오]))", RegexOptions.Compiled | RegexOptions.Multiline),
                        Lang = "kr",
                        Yes = "예",
                        No = "아니오"
                    },
                    new YesNoInfo
                    {
                        Regex = new Regex("(yes|yeah)(no|nah)",
                            RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase),
                        Lang = "en",
                        Yes = "Yes",
                        No = "No"
                    },
                    new YesNoInfo
                    {
                        Regex = new Regex("[はハﾊ][いイｲ]+[えエｴ]", RegexOptions.Compiled | RegexOptions.Multiline),
                        Lang = "jp",
                        Yes = "はい",
                        No = "いいえ"
                    }
                };
            }
            
            YesNoInfo? yni = pipeYesNoRegex.FirstOrDefault(item => item.Regex.IsMatch(status.Content));
            // ReSharper disable once ConditionIsAlwaysTrueOrFalse // false positive
            if (yni == null)
            {
                await next();
                return;
            }

            double randNum = _rand.NextDouble();
            string replyText = $"{((randNum >= 0.5) ? yni.Yes : yni.No)} ({Math.Round(randNum * 200 - 100)}%)";
            await ReplyTo(status, replyText);
        }
        
        private async Task PipeDice(INote status, Func<Task> next)
        {
            string diceExpression = status.Content.Trim();
            string[] diceExprList = diceExpression.Split(new[] { ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            var matchQuery = diceExprList.Select(expr => Regex.Match(expr, "([0-9]*?)[dD]([0-9]+)"));
            var matchList = matchQuery as Match[] ?? matchQuery.ToArray();
            
            if (matchList.Any(match => !match.Success) || matchList.Length == 0)
            {
                await next();
                return;
            }
            
            const string diceErrorMsg = "주사위 숫자를 확인할 수 없습니다. 숫자가 1보다 큰지 확인해보세요.";

            try
            {
                string[] diceResults = matchList.Select(match =>
                {
                    string diceCountStr = match.Groups[1].Value;
                    diceCountStr = string.IsNullOrWhiteSpace(diceCountStr) ? "1" : diceCountStr;
                    string diceNumStr = match.Groups[2].Value;

                    int diceCount;
                    int diceNum;

                    diceCount = int.Parse(diceCountStr);
                    diceNum = int.Parse(diceNumStr);
                    if (diceNum <= 1)
                    {
                        throw new ArgumentException("diceNum is less than or equal to 1");
                    }
		    int[] diceList = Enumerable.Repeat(0, diceCount).Select(_ => _rand.Next(1, diceNum + 1)).ToArray();
                    int diceSum = diceList.Sum();

                    string diceRollStr = string.Join(" + ", diceList);

                    return $"{diceRollStr} = {diceSum} ({diceNum}면체)";
                }).ToArray();

                string diceReplyStr = string.Join("\r\n", diceResults);
                if (diceResults.Length > 1)
                {
                    diceReplyStr = "\r\n" + diceReplyStr;
                }
                
                await ReplyTo(status, diceReplyStr);
            }
            catch (Exception ex)
            {
                //await ReplyTo(status, diceErrorMsg + "\r\n\r\n" + $"에러 메시지: {ex.Message}");
                await ReplyTo(status, "잘못된 사용법입니다. 이렇게 해보세요:\r\n\r\n" + HelpText);
                return;
            }
        }
        
        private async Task PipeChoice(INote status, Func<Task> next)
        {
            string[] selectable = _ParseToSelectableItems(status.Content);

            if (!selectable.Any())
            {
                await next();
                return;
            }
            
            string selection = selectable[_rand.Next(selectable.Count())].Trim();
            
            if (selectable.Length == 1)
            {
                selection += "\r\n(선택할 항목이 하나밖에 없는 것 같습니다.)";
            }
            
            await ReplyTo(status, selection);
        }
        
        private async Task PipeNotHandledHelp(INote status, Func<Task> next)
        {
            await ReplyTo(status, "선택할 게 없는 것 같습니다. 이렇게 해보세요:\r\n\r\n" + HelpText);
        }
        
        private static readonly Lazy<Regex> VsSepRegex
            = new Lazy<Regex>(() => new Regex("((^|[ \r\n]+)([Vv][Ss]\\.?)(($|[ \r\n]+)([Vv][Ss]\\.?))*($|[ \r\n]+))", RegexOptions.ExplicitCapture | RegexOptions.Compiled));
        private static readonly Lazy<Regex> NewLineSepRegex
            = new Lazy<Regex>(() => new Regex("[\r\n]", RegexOptions.ExplicitCapture | RegexOptions.Compiled));
        private static string[] _ParseToSelectableItems(string statusText)
        {
            statusText = statusText.Trim();
            
            string[] selectable;

            if (VsSepRegex.Value.IsMatch(statusText))
            {
                selectable = VsSepRegex.Value.Split(statusText);
                selectable = selectable.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).ToArray();
            }
            else if (NewLineSepRegex.Value.IsMatch(statusText))
            {
                selectable = statusText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            }
            else
            {
                selectable = statusText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            }

            return selectable;
        }
    }
}
