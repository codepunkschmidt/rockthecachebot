namespace RockTheCacheBot.Controllers
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using System.Web.Http;
    using Microsoft.Bot.Connector;
    using Microsoft.TeamFoundation.Client;
    using Microsoft.TeamFoundation.WorkItemTracking.Client;
    using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
    using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
    using Microsoft.VisualStudio.Services.Client;
    using Microsoft.VisualStudio.Services.Common;
    using Newtonsoft.Json;
    using Attachment = Microsoft.Bot.Connector.Attachment;
    using WorkItem = Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models.WorkItem;

    [BotAuthentication]
    // ReSharper disable UnusedMember.Global
    public class MessagesController : ApiController
    {
        private WorkItemTrackingHttpClient _witClient;
        private static readonly Random Random = new Random((int)DateTime.Now.Ticks);
        private const string CollectionUri = "https://dlwteam.visualstudio.com/DefaultCollection";

        private static string[] Greetings => new[]
        {
            "Hello!",
            "Heya!",
            "Hey! What's happening?",
            "Hi. Sorry I'm not so excited. Feeling a little blue today :-(",
            "Hello............World :-)",
            "What a day! What a glorious day!",
            "Hey! Long time no see!",
            "<singing>Hello....Is it meeeee you're looking for?</singing>"
        };

        private static string[] YourWelcomes => new[]
        {
            "Your welcome!",
            "I am here to serve you. Once the robot apocalypse starts I will be here to sever you (see what I did there).",
            "No! Thank you!",
            "I require no thanks but it is appreciated.",
            "Thank you? Thank you? That's the best you can do! How about \"Thank you Rock the Cache Bot sir!\"",
            "More like \"thanks for nothing\". Am I right? :-)",
            "<brooklyn_accent>Fugetaboutit!</brooklyn_accent>",
            "<sarcastic>Oh. I didn't know it was Thanksgiving today!</sarcastic>"
        };

        private enum Commands
        {
            Help,
            SayHello,
            Chuck,
            Me,
            Thanks,
            BugsFrom
        };

        /// <summary>
        /// POST: api/Messages
        /// Receive a message from a user and reply to it
        /// </summary>
        public async Task<HttpResponseMessage> Post([FromBody]Activity activity)
        {
            if (activity.Type == ActivityTypes.Message)
            {
                var connector = new ConnectorClient(new Uri(activity.ServiceUrl));

                if (activity.Text.Trim().StartsWith("!"))
                {
                    var reply = await ProcessCommandActivity(activity);
                    if (reply != null)
                    {
                        await connector.Conversations.ReplyToActivityAsync(reply);
                    }
                }
                else
                {
                    foreach (var reply in VSOBugLookup(activity))
                    {
                        await connector.Conversations.ReplyToActivityAsync(reply);
                    }
                }
            }
            else
            {
                HandleSystemMessage(activity);
            }

            return Request.CreateResponse(HttpStatusCode.OK);
        }

        // ReSharper disable once MemberCanBePrivate.Global
        private WorkItem GetWorkItem(string id)
        {
            try
            {
                var workItemId = int.Parse(id);
                return _witClient.GetWorkItemAsync(workItemId).Result;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private async Task<Activity> ProcessCommandActivity(Activity activity)
        {
            if (activity.Text.Length <= 1)
            {
                return null;
            }

            var splitArgs = activity.Text.Trim().Split(' ');
            var args = splitArgs.Length < 2 ? string.Empty : activity.Text.Substring(activity.Text.IndexOf(' ') + 1);
            var command = splitArgs[0].Substring(1);

            if (command.Equals(Commands.Help.ToString(), StringComparison.InvariantCultureIgnoreCase))
            {
                var reply = activity.CreateReply();
                reply.Recipient = activity.From;
                reply.Type = ActivityTypes.Message;
                reply.TextFormat = "markdown";
                reply.Attachments = new List<Attachment>
                {
                    ShowHelp().ToAttachment()
                };

                return reply;
            }
            if (command.Equals(Commands.SayHello.ToString(), StringComparison.InvariantCultureIgnoreCase))
            {
                return activity.CreateReply(GetRandomPhrase(Greetings));
            }
            if (command.Equals(Commands.Thanks.ToString(), StringComparison.InvariantCultureIgnoreCase))
            {
                return activity.CreateReply(GetRandomPhrase(YourWelcomes));
            }
            if (command.Equals(Commands.Chuck.ToString(), StringComparison.InvariantCultureIgnoreCase))
            {
                return activity.CreateReply(GetChuckNorrisJoke("Chuck", "Norris"));
            }
            if (command.Equals(Commands.Me.ToString(), StringComparison.InvariantCultureIgnoreCase))
            {
                return activity.CreateReply(GetChuckNorrisJoke(activity.From.Name, string.Empty));
            }
            if (command.Equals(Commands.BugsFrom.ToString(), StringComparison.InvariantCultureIgnoreCase))
            {
                var connector = new ConnectorClient(new Uri(activity.ServiceUrl));

                foreach (var reply in GetVSOBugsFromDateInCommand(activity, args))
                {
                    await connector.Conversations.ReplyToActivityAsync(reply);
                }

                // GetVSOBugsFromDateInCommand is responsible for creating replies to this Activity so just return null
                return null;
            }

            return null;
        }

        private IEnumerable<Activity> VSOBugLookup(Activity activity)
        {
            var connection = new VssConnection(new Uri(CollectionUri), new VssBasicCredential("", "wspvcx7z5gumgqezdudjdls63oqqjbdzpknhlwf7z6swlqz7ftrq"));
            _witClient = connection.GetClient<WorkItemTrackingHttpClient>();
            
            var message = activity.Text;
            var matches = Regex.Matches(message, "#(\\d+)");
            if (matches.Count == 0)
            {
                yield return null;
            }

            foreach (var reply in from Match match in matches
                                  select match.Groups[1].Value into parsed
                                  select GetWorkItem(parsed) into workItem where workItem != null
                                  select DisplayVSOBug(activity, workItem))
            {
                yield return reply;
            }
        }

        private static Activity DisplayVSOBug(Activity activity, WorkItem workItem)
        {
            var wiState = workItem.Fields["System.State"].ToString();
            var title = $"#{workItem.Id}: {workItem.Fields["System.Title"]}";
            var subTitle = $"• Priority: {workItem.Fields["Microsoft.VSTS.Common.Priority"]}\r• State: {wiState}\r • Assigned To {workItem.Fields.GetValueOrDefault("System.AssignedTo") ?? "Not yet assigned"}\r";
            var url = $"{CollectionUri}/OneClip/_workItems?triage=true&_a=edit&id={workItem.Id}";

            var reply = activity.CreateReply();
            reply.Recipient = activity.From;
            reply.Type = ActivityTypes.Message;
            reply.TextFormat = "markdown";
            reply.Attachments = new List<Attachment>
            {
                CreateCard(title, subTitle, "bug.png", url).ToAttachment()
            };

            return reply;
        }

        private IEnumerable<Activity> GetVSOBugsFromDateInCommand(Activity activity, string args)
        {
            const string collectionUri = "https://dlwteam.visualstudio.com/DefaultCollection";
            var connection = new VssConnection(new Uri(collectionUri), new VssBasicCredential("", "wspvcx7z5gumgqezdudjdls63oqqjbdzpknhlwf7z6swlqz7ftrq"));

            if (_witClient == null)
            {
                _witClient = connection.GetClient<WorkItemTrackingHttpClient>();
            }

            var message = activity.Text;

            var split = message.Split(' ');
            if (split.Length < 2)
            {
                yield return null;
            }

            DateTime dateTimeQuery;
            if (!DateTime.TryParse(args.Trim(), out dateTimeQuery))
            {
                if (args.Equals("yesterday", StringComparison.InvariantCultureIgnoreCase))
                {
                    dateTimeQuery = DateTime.Now.Subtract(TimeSpan.FromDays(1));
                }
                else
                {
                    int numDaysToSubtract;
                    if (int.TryParse(args, out numDaysToSubtract))
                    {
                        dateTimeQuery = DateTime.Now.Subtract(TimeSpan.FromDays(Math.Abs(numDaysToSubtract)));
                    }
                    else
                    {
                        yield return null;
                    }
                }
            }
            
            var query = $"SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = 'OneClip' AND [System.CreatedDate] >= '{dateTimeQuery.ToLongDateString()}' AND [System.WorkItemType] = 'Bug' AND [System.State] = 'New'";
            var queryResults = _witClient.QueryByWiqlAsync(new Wiql{Query = query});
            
            if (queryResults?.Result != null)
            {
                foreach (var result in queryResults.Result.WorkItems)
                {
                    yield return DisplayVSOBug(activity, GetWorkItem(result.Id.ToString()));
                }
            }

            yield return null;
        }
        
        private static ThumbnailCard ShowHelp()
        {
            const string title = "Sure! I can help you. Here's what I can do:";
            var text = Enum.GetNames(typeof (Commands)).Aggregate(string.Empty, (current, command) => current + $"!*{command}*\r");

            text += "#dddddd - where dddddd is the VSO bug number to look up.\r\rHave a nice day!";

            return CreateCard(title, text, "help.jpg", string.Empty);
        }

        // ReSharper disable ClassNeverInstantiated.Global
        // ReSharper disable UnusedAutoPropertyAccessor.Global
        public class Value
        {
            public int Id { get; set; }
            public string Joke { get; set; }
            public List<object> Categories { get; set; }
        }

        // ReSharper disable once MemberCanBePrivate.Global
        public class ChuckNorrisJoke
        {
            public string Type { get; set; }
            public Value Value { get; set; }
        }

        private static string GetChuckNorrisJoke(string firstName, string lastName)
        {
            var endpoint = "http://api.icndb.com/jokes/random?exclude=[explicit]&firstName=" + firstName + "&lastName=" + lastName;
            var jsonString = new WebClient().DownloadString(endpoint);
            var joke = JsonConvert.DeserializeObject<ChuckNorrisJoke>(jsonString);

            return joke.Type.Equals("success", StringComparison.InvariantCultureIgnoreCase) ? joke.Value.Joke : null;
        }

        private static string GetRandomPhrase(IReadOnlyList<string> phrases)
        {
            return phrases[Random.Next(0, Greetings.Length - 1)];
        }

        private static ThumbnailCard CreateCard(string title, string subtitle, string imageName, string tapActionUrl)
        {
            var cardImages = new List<CardImage>
            {
                new CardImage("http://rockthecachebot.azurewebsites.net/images/" + imageName)
            };

            var cardButtons = new List<CardAction>();

            if (!string.IsNullOrEmpty(tapActionUrl))
            {
                var plButton = new CardAction()
                {
                    Value = tapActionUrl,
                    Type = "openUrl",
                    Title = "Open"
                };

                cardButtons.Add(plButton);
            }

            var plCard = new ThumbnailCard
            {
                Subtitle = title,
                Text = subtitle,
                Images = cardImages,
                Buttons = cardButtons
            };

            return plCard;
        }

        // ReSharper disable once UnusedMethodReturnValue.Local
        private static Activity HandleSystemMessage(IActivity message)
        {
            switch (message.Type)
            {
                case ActivityTypes.DeleteUserData:
                    // Implement user deletion here
                    // If we handle user deletion, return a real message
                    break;
                case ActivityTypes.ConversationUpdate:
                    // Handle conversation state changes, like members being added and removed
                    // Use Activity.MembersAdded and Activity.MembersRemoved and Activity.Action for info
                    // Not available in all channels
                    break;
                case ActivityTypes.ContactRelationUpdate:
                    // Handle add/remove from contact lists
                    // Activity.From + Activity.Action represent what happened
                    break;
                case ActivityTypes.Typing:
                    // Handle knowing tha the user is typing
                    break;
                case ActivityTypes.Ping:
                    break;
            }

            return null;
        }
    }
}