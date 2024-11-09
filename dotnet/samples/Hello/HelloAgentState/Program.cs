// Copyright (c) Microsoft Corporation. All rights reserved.
// Program.cs

using System.Text.Json;
using Microsoft.AutoGen.Abstractions;
using Microsoft.AutoGen.Agents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// send a message to the agent
var app = await AgentsApp.PublishMessageAsync("HelloAgents", new NewMessageReceived
{
    Message = "World"
}, local: true);

await app.WaitForShutdownAsync();

namespace Hello
{
    [TopicSubscription("HelloAgents")]
    public class HelloAgent(
        IAgentRuntime context,
        [FromKeyedServices("EventTypes")] EventTypes typeRegistry) : ConsoleAgent(
            context,
            typeRegistry),
            IHandle<NewMessageReceived>,
            IHandle<ConversationClosed>,
            IHandle<Shutdown>
    {
        private AgentState? State { get; set; }
        public async Task Handle(NewMessageReceived item)
        {
            var response = await SayHello(item.Message).ConfigureAwait(false);
            var evt = new Output
            {
                Message = response
            }.ToCloudEvent(this.AgentId.Key);
            Dictionary <string, string> state = new()
            {
                { "data", "We said hello to " + item.Message },
                { "workflow", "Active" }
            };
            await StoreAsync(new AgentState
            {
                AgentId = this.AgentId,
                TextData = JsonSerializer.Serialize(state)
            }).ConfigureAwait(false);
            await PublishEventAsync(evt).ConfigureAwait(false);
            var goodbye = new ConversationClosed
            {
                UserId = this.AgentId.Key,
                UserMessage = "Goodbye"
            }.ToCloudEvent(this.AgentId.Key);
            await PublishEventAsync(goodbye).ConfigureAwait(false);
            // send the shutdown message
            await PublishEventAsync(new Shutdown { Message = this.AgentId.Key }.ToCloudEvent(this.AgentId.Key)).ConfigureAwait(false);

        }
        public async Task Handle(ConversationClosed item)
        {
            State = await ReadAsync<AgentState>(this.AgentId).ConfigureAwait(false);
            var state = JsonSerializer.Deserialize<Dictionary<string, string>>(State.TextData) ?? new Dictionary<string, string> { { "data", "No state data found" } };
            var goodbye = $"\nState: {state}\n*********************  {item.UserId} said {item.UserMessage}  ************************";
            var evt = new Output
            {
                Message = goodbye
            }.ToCloudEvent(this.AgentId.Key);
            await PublishEventAsync(evt).ConfigureAwait(true);
            state["workflow"] = "Complete";
            await StoreAsync(new AgentState
            {
                AgentId = this.AgentId,
                TextData = JsonSerializer.Serialize(state)
            }).ConfigureAwait(false);
        }
        public async Task Handle(Shutdown item)
        {
            string? workflow = null;
            // make sure the workflow is finished
            while (workflow != "Complete")
            {
                State = await ReadAsync<AgentState>(this.AgentId).ConfigureAwait(true);
                var state = JsonSerializer.Deserialize<Dictionary<string, string>>(State?.TextData ?? "{}") ?? new Dictionary<string, string>();
                workflow = state["workflow"];
                await Task.Delay(1000).ConfigureAwait(false);
            }
            // now we can shut down...
            await AgentsApp.ShutdownAsync().ConfigureAwait(true);
        }
        public async Task<string> SayHello(string ask)
        {
            var response = $"\n\n\n\n***************Hello {ask}**********************\n\n\n\n";
            return response;
        }
    }
}
