//
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//

#nullable disable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SqlServer.SqlCopilot.Cartridges;
using Microsoft.SqlServer.SqlCopilot.Common;
using Microsoft.SqlTools.Connectors.VSCode;
using Microsoft.SqlTools.ServiceLayer.Connection;
using Microsoft.SqlTools.ServiceLayer.Copilot.Contracts;

namespace Microsoft.SqlTools.ServiceLayer.Copilot
{
    public class CopilotConversation
    {
        public string ConversationUri { get; set; }

        public string CurrentMessage { get; set; }

        public SqlConnection SqlConnection { get; set; }

        public AutoResetEvent MessageCompleteEvent { get; set; }
    }

    public class CopilotConversationManager
    {
        private static StringBuilder responseHistory = new StringBuilder();
        private static ConcurrentDictionary<string, CancellationTokenSource> _activeConversations = new ConcurrentDictionary<string, CancellationTokenSource>();

        private readonly ConcurrentDictionary<string, CopilotConversation> conversations = new();
        private readonly ChatMessageQueue messageQueue;
        private readonly Dictionary<string, SqlToolsRpcClient> rpcClients = new();
        private readonly ActivitySource activitySource = new("SqlCopilot");
        private Kernel userSessionKernel;
        private IChatCompletionService userChatCompletionService;
        private ChatHistory chatHistory;
        private static VSCodePromptExecutionSettings openAIPromptExecutionSettings;


        public CopilotConversationManager()
        {
            messageQueue = new ChatMessageQueue(conversations);
            InitializeTraceSource();
        }

        public LLMRequest RequestLLM(
            string conversationUri,
            IList<LanguageModelRequestMessage> messages,
            IList<LanguageModelChatTool> tools,
            AutoResetEvent responseReadyEvent)
        {
            return messageQueue.EnqueueRequest(
                conversationUri, messages, tools, responseReadyEvent);
        }

        public GetNextMessageResponse GetNextMessage(
            string conversationUri,
            string userText,
            LanguageModelChatTool tool,
            string toolParameters)
        {
            return messageQueue.ProcessNextMessage(
                conversationUri, userText, tool, toolParameters);
        }

        private void InitializeTraceSource()
        {
            // make sure any console listeners are configured to write to stderr so that the 
            // JSON RPC channel over stdio isn't clobbered.  If there is no console
            // listener then add one for stderr
            bool consoleListnerFound = false;
            foreach (TraceListener listener in SqlCopilotTrace.Source.Listeners)
            {
                if (listener is ConsoleTraceListener)
                {
                    // Change the ConsoleTraceListener to write to stderr instead of stdout
                    ((ConsoleTraceListener)listener).Writer = Console.Error;
                    consoleListnerFound = true;
                }
            }

            // if there is no console listener, add one for stderr
            if (!consoleListnerFound)
            {
                SqlCopilotTrace.Source.Listeners.Add(new ConsoleTraceListener() { Writer = Console.Error, TraceOutputOptions = TraceOptions.DateTime });
            }

            // Set trace level for the console output
            SqlCopilotTrace.Source.Switch.Level = SourceLevels.Information;
        }

        private void InitConversation(CopilotConversation conversation)
        {
            SqlCopilotTrace.WriteInfoEvent(SqlCopilotTraceEvents.ChatSessionStart, "Initializing Chat Session");

            try
            {
                // Create our services
                var sqlService = new SqlExecutionService(conversation.SqlConnection);
                var responseHandler = new ChatResponseHandler(conversation);
                var rpcClient = new SqlToolsRpcClient(sqlService, responseHandler);
                rpcClients[conversation.ConversationUri] = rpcClient;

                // Setup the kernel
                openAIPromptExecutionSettings = new VSCodePromptExecutionSettings
                {
                    Temperature = 0.0,
                    ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
                    MaxTokens = 1500
                };

                var builder = Kernel.CreateBuilder();
                builder.AddVSCodeChatCompletion(new VSCodeLanguageModelEndpoint());

                userSessionKernel = builder.Build();
                activitySource.StartActivity("Main");

                // Setup access and tools
                var accessChecker = new ExecutionAccessChecker(userSessionKernel);
                var sqlExecHelper = new SqlExecAndParse(rpcClient, accessChecker);
                var currentDbConfig = SqlExecAndParse.GetCurrentDatabaseAndServerInfo().Result;

                // Add tools to kernel
                builder.Plugins.AddFromObject(sqlExecHelper);

                // Setup cartridges
                var services = new ServiceCollection();
                services.AddSingleton<SQLPluginHelperResourceServices>();

                var cartridgeBootstrapper = new Bootstrapper(rpcClient, accessChecker);
                cartridgeBootstrapper.LoadCartridges(
                    services.BuildServiceProvider().GetRequiredService<SQLPluginHelperResourceServices>());
                cartridgeBootstrapper.LoadToolSets(builder, currentDbConfig);

                // Rebuild kernel with all plugins
                userSessionKernel = builder.Build();
                userChatCompletionService = userSessionKernel.GetRequiredService<IChatCompletionService>();

                // Initialize chat history
                var initialSystemMessage = @"System message: YOUR ROLE:
You are an AI copilot assistant running inside Visual Studio Code and connected to a specific SQL Server database.
Act as a SQL Server and VS Code SME.

GENERAL REQUIREMENTS:
- Work step-by-step, do not skip any requirements.
- **Important**: Do not re-call the same tool with identical parameters unless specifically prompted.
- **Important**: Do not assume a default schema (e.g. dbo) for database objects when calling tool functions.  Use the schema discovery tool to find the correct schema before calling other tools.
- If a tool has been successfully called, move on to the next step based on the user's query.";

                chatHistory = new ChatHistory(initialSystemMessage);

                SqlExecAndParse.SetAccessMode(CopilotAccessModes.READ_WRITE_NEVER);
                chatHistory.AddSystemMessage(
                    $"Configuration information for currently connected database: {currentDbConfig}");

                // Wire up response handler events
                responseHandler.OnChatResponse += (e) =>
                {
                    switch (e.UpdateType)
                    {
                        case ResponseUpdateType.PartialResponseUpdate:
                            e.Conversation.CurrentMessage += e.PartialResponse;
                            break;
                        case ResponseUpdateType.Completed:
                            e.Conversation.MessageCompleteEvent.Set();
                            break;
                    }
                };
            }
            catch (Exception e)
            {
                SqlCopilotTrace.WriteErrorEvent(
                    SqlCopilotTraceEvents.ChatSessionFailed,
                    e.Message);
            }
        }

        public async Task<bool> StartConversation(string conversationUri, string connectionUri, string userText)
        {
            // Get DB connection
            DbConnection dbConnection = await ConnectionService.Instance.GetOrOpenConnection(
                connectionUri, ConnectionType.Default);

            if (!ConnectionService.Instance.TryGetAsSqlConnection(dbConnection, out var sqlConnection))
                return false;

            // Create and initialize conversation
            var conversation = new CopilotConversation
            {
                ConversationUri = conversationUri,
                SqlConnection = sqlConnection,
                CurrentMessage = string.Empty,
                MessageCompleteEvent = new AutoResetEvent(false)
            };

            InitConversation(conversation);
            conversations.AddOrUpdate(conversationUri, conversation, (_, _) => conversation);

            // Start processing in background
            _ = Task.Run(async () =>
                await SendUserPromptStreamingAsync(userText, conversationUri));

            return true;
        }

        /// <summary>
        /// Event handler for the chat response received event from the SqlCopilotClient.
        /// This is then forwarded onto the SSMS specific events.  This is so in case the SQL 
        /// Copilot API changes, the impact is localized to this class.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnChatResponse(object sender, ChatResponseEventArgs e)
        {
            ChatResponseEventArgs chatResponseArgs = new ChatResponseEventArgs();
            // forward the event onto the SSMS specific events the UI subscribes to
            switch (e.UpdateType)
            {
                case ResponseUpdateType.PartialResponseUpdate:
                    e.Conversation.CurrentMessage += e.PartialResponse;
                    break;

                case ResponseUpdateType.Completed:
                    e.Conversation.MessageCompleteEvent.Set();
                    break;

                case ResponseUpdateType.Started:
                    break;

                case ResponseUpdateType.Canceled:
                    break;
            }
        }

        public async Task<RpcResponse<string>> SendUserPromptStreamingAsync(string userPrompt, string chatExchangeId)
        {
            if (chatHistory == null)
            {
                var errorMessage = "Chat history not initialized.  Call InitializeAsync first.";
                SqlCopilotTrace.WriteErrorEvent(SqlCopilotTraceEvents.PromptReceived, errorMessage);
                return new RpcResponse<string>(SqlCopilotRpcReturnCodes.GeneralError, errorMessage);
            }

            if (userChatCompletionService == null) // || _plannerChatCompletionService == null || _databaseQueryChatCompletionService == null)
            {
                var errorMessage = "Chat completion service not initialized.  Call InitializeAsync first.";
                SqlCopilotTrace.WriteErrorEvent(SqlCopilotTraceEvents.PromptReceived, errorMessage);
                return new RpcResponse<string>(SqlCopilotRpcReturnCodes.GeneralError, errorMessage);
            }

            var sqlExecuteRpcParent = rpcClients[chatExchangeId];
            if (sqlExecuteRpcParent == null)
            {
                var errorMessage = "Communication channel not configured.  Call InitializeAsync first.";
                SqlCopilotTrace.WriteErrorEvent(SqlCopilotTraceEvents.PromptReceived, errorMessage);
                return new RpcResponse<string>(SqlCopilotRpcReturnCodes.GeneralError, errorMessage);
            }

            SqlCopilotTrace.WriteInfoEvent(SqlCopilotTraceEvents.PromptReceived, $"User prompt received.");
            SqlCopilotTrace.WriteVerboseEvent(SqlCopilotTraceEvents.PromptReceived, $"Prompt: {userPrompt}");

            // track this exchange and its cancellation token
            SqlCopilotTrace.WriteInfoEvent(SqlCopilotTraceEvents.PromptReceived, $"Tracking exchange {chatExchangeId}.");
            var cts = new CancellationTokenSource();
            _activeConversations.TryAdd(chatExchangeId, cts);

            // notify client of ID for this exchange
            await sqlExecuteRpcParent.InvokeAsync<string>("ChatExchangeStartedAsync", chatExchangeId);

            // setup the default response
            RpcResponse<string> promptResponse = new RpcResponse<string>("Success");

            try
            {
                chatHistory.AddUserMessage(userPrompt);
                StringBuilder completeResponse = new StringBuilder();

                var result = userChatCompletionService.GetStreamingChatMessageContentsAsync(chatHistory,
                    executionSettings: openAIPromptExecutionSettings,
                    kernel: userSessionKernel,
                    cts.Token);
                SqlCopilotTrace.WriteInfoEvent(SqlCopilotTraceEvents.PromptReceived, $"Prompt submitted to kernel.");

                // loop on the IAsyncEnumerable to get the results
                SqlCopilotTrace.WriteVerboseEvent(SqlCopilotTraceEvents.ResponseGenerated, "Response:");
                for (int awaitAttempts = 0; awaitAttempts < 10; awaitAttempts++)
                {
                    await foreach (var content in result)
                    {
                        if (!string.IsNullOrEmpty(content.Content))
                        {
                            SqlCopilotTrace.WriteVerboseEvent(SqlCopilotTraceEvents.ResponseGenerated, content.Content);
                            completeResponse.Append(content.Content);
                            await sqlExecuteRpcParent.InvokeAsync<string>("HandlePartialResponseAsync", chatExchangeId, content.Content);
                        }
                    }

                    if (completeResponse.Length > 0)
                    {
                        // got the response, break out of the retry loop
                        break;
                    }
                }

                if (cts.IsCancellationRequested)
                {
                    // client cancelled the request
                    SqlCopilotTrace.WriteInfoEvent(SqlCopilotTraceEvents.PromptReceived, $"Client canceled exchange {chatExchangeId}.");
                    await sqlExecuteRpcParent.InvokeAsync<string>("ChatExchangeCanceledAsync", chatExchangeId);

                    // change the prompt response to indicate the cancellation
                    promptResponse = new RpcResponse<string>(SqlCopilotRpcReturnCodes.TaskCancelled, "Request canceled by client.");
                }
                else
                {
                    // add the assistant response to the chat history
                    SqlCopilotTrace.WriteInfoEvent(SqlCopilotTraceEvents.ResponseGenerated, "Response completed. Updating chat history.");
                    await sqlExecuteRpcParent.InvokeAsync<string>("ChatExchangeCompleteAsync", chatExchangeId);
                    chatHistory.AddAssistantMessage(completeResponse.ToString());
                    responseHistory.Append(completeResponse);
                }
            }
            catch (HttpOperationException e)
            {
                SqlCopilotTrace.WriteErrorEvent(SqlCopilotTraceEvents.CopilotServerException, e.Message);
                return new RpcResponse<string>(SqlCopilotRpcReturnCodes.ApiException, e.Message);
            }
            finally
            {

                // remove this exchange ID from the active conversations
                SqlCopilotTrace.WriteInfoEvent(SqlCopilotTraceEvents.PromptReceived, $"Removing exchange {chatExchangeId} from active conversations.");
                _activeConversations.Remove(chatExchangeId, out cts);
            }

            return promptResponse;
        }
    }

    /// <summary>
    /// event data for a processing update message
    /// </summary>
    public class ProcessingUpdateEventArgs : EventArgs
    {
        /// <summary>
        /// the unique ID of the chat exchange this completion event is for
        /// </summary>
        public string ChatExchangeId { get; set; }

        /// <summary>
        /// The type of processing update message
        /// </summary>
        public ProcessingUpdateType UpdateType { get; set; }

        /// <summary>
        /// The details for the processing update.  Callers should be resilient to extra parameters being added in the future or not always being present.
        /// </summary>
        public Dictionary<string, string> Parameters { get; set; }
    }

    /// <summary>
    /// the type of procsessing update the event is for
    /// </summary>
    public enum ResponseUpdateType
    {
        /// <summary>
        /// processing of the prompt has started
        /// </summary>
        Started,

        /// <summary>
        /// processing of the prompt has completed
        /// </summary>
        Completed,

        /// <summary>
        /// a partial response has been generated (streaming)
        /// </summary>
        PartialResponseUpdate,

        /// <summary>
        /// the prompt processing has been canceled (user initiated)
        /// </summary>
        Canceled
    }

    /// <summary>
    /// common event data for chat response events
    /// </summary>
    public class ChatResponseEventArgs : EventArgs
    {
        /// <summary>
        /// type of processing update event
        /// </summary>
        public ResponseUpdateType UpdateType { get; set; }

        /// <summary>
        /// the unique ID of the chat exchange this completion event is for
        /// </summary>
        public string ChatExchangeId { get; set; }

        /// <summary>
        /// the partial response if it is a partial response event
        /// </summary>
        public string PartialResponse { get; set; }

        public CopilotConversation Conversation { get; set; }
    }
}
