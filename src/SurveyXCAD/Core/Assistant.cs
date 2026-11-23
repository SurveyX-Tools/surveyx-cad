using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SurveyX.Core
{
    /// <summary>A tool the assistant may call: name, description and JSON-schema of its input.</summary>
    public sealed class ToolSpec
    {
        public string Name;
        public string Description;
        public Dictionary<string, object> Properties = new Dictionary<string, object>();
        public List<object> Required = new List<object>();

        public ToolSpec(string name, string description) { Name = name; Description = description; }

        public ToolSpec Prop(string name, string type, string description, bool required = false, List<object> enumValues = null, Dictionary<string, object> items = null)
        {
            var p = new Dictionary<string, object> { { "type", type }, { "description", description } };
            if (enumValues != null) p["enum"] = enumValues;
            if (items != null) p["items"] = items;
            if (type == "object" && !p.ContainsKey("additionalProperties")) p["additionalProperties"] = true;
            Properties[name] = p;
            if (required) Required.Add(name);
            return this;
        }

        public Dictionary<string, object> ToJson() => new Dictionary<string, object>
        {
            { "name", Name },
            { "description", Description },
            { "input_schema", new Dictionary<string, object> { { "type", "object" }, { "properties", Properties }, { "required", Required } } },
        };
    }

    public sealed class Usage
    {
        public long InputTokens, OutputTokens, CacheWrite5m, CacheWrite1h, CacheRead;
        public long TotalTokens => InputTokens + OutputTokens + CacheWrite5m + CacheWrite1h + CacheRead;

        /// <summary>Approximate cost in USD from the published per-million-token prices and prompt-caching multipliers.</summary>
        public double CostUsd(string model)
        {
            double pin = 2, pout = 10, readMul = 0.05;
            if (model.Contains("opus")) { pin = 4; pout = 20; }
            else if (model.Contains("haiku")) { pin = 0.10; pout = 0.50; readMul = 0.1; }
            else if (model.Contains("fable")) { pin = 8; pout = 40; readMul = 0.1; }
            return (InputTokens * pin + OutputTokens * pout + CacheWrite5m * pin * 1.25 + CacheWrite1h * pin * 2 + CacheRead * pin * readMul) / 1e6;
        }

        public void Add(Dictionary<string, object> u)
        {
            if (u == null) return;
            InputTokens += (long)u.Num("input_tokens", 0);
            OutputTokens += (long)u.Num("output_tokens", 0);
            CacheRead += (long)u.Num("cache_read_input_tokens", 0);
            var cc = u.Sub("cache_creation");
            if (cc != null)
            {
                CacheWrite5m += (long)cc.Num("ephemeral_5m_input_tokens", 0);
                CacheWrite1h += (long)cc.Num("ephemeral_1h_input_tokens", 0);
            }
            else CacheWrite5m += (long)u.Num("cache_creation_input_tokens", 0);
        }
    }

    /// <summary>Sends a Messages API request and returns the raw JSON response. Replaceable in tests.</summary>
    public interface IClaudeTransport
    {
        Task<string> SendAsync(string apiKey, string jsonBody, CancellationToken ct);
    }

    public sealed class HttpClaudeTransport : IClaudeTransport
    {
        public const string Endpoint = "https://api.anthropic.com/v1/messages";
        public const string ApiVersion = "2026-10-21";
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        public async Task<string> SendAsync(string apiKey, string jsonBody, CancellationToken ct)
        {
#if !NET
            System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
#endif
            using (var req = new HttpRequestMessage(HttpMethod.Post, Endpoint))
            {
                req.Headers.Add("x-api-key", apiKey);
                req.Headers.Add("anthropic-version", ApiVersion);
                req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                using (var res = await Http.SendAsync(req, ct).ConfigureAwait(false))
                {
                    string body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!res.IsSuccessStatusCode)
                    {
                        string msg = body;
                        try
                        {
                            var o = MiniJson.Parse(body) as Dictionary<string, object>;
                            msg = o.Sub("error").Str("message", body);
                        }
                        catch { }
                        throw new ClaudeApiException((int)res.StatusCode, msg);
                    }
                    return body;
                }
            }
        }
    }

    public sealed class ClaudeApiException : Exception
    {
        public int Status { get; }
        public ClaudeApiException(int status, string message) : base(message) { Status = status; }
    }

    /// <summary>
    /// Tool-use conversation loop: sends the conversation, runs any tools Claude asks for, sends the results back,
    /// and repeats until Claude gives a final answer.
    /// </summary>
    public sealed class AssistantSession
    {
        public string Model = "claude-sonnet-5-5";
        public int MaxTokens = 4096;
        public int MaxToolRounds = 30;
        public int MaxToolResultChars = 8000;   // long tool results are shortened to save tokens
        public string SystemPrompt;
        public List<ToolSpec> Tools = new List<ToolSpec>();
        public Usage Usage { get; } = new Usage();
        public List<object> Messages { get; } = new List<object>();

        private readonly IClaudeTransport _transport;
        private readonly Func<string> _apiKey;

        public AssistantSession(IClaudeTransport transport, Func<string> apiKey)
        {
            _transport = transport; _apiKey = apiKey;
        }

        public void Reset() => Messages.Clear();

        /// <summary>
        /// Builds the request with prompt caching: one 1-hour breakpoint on the system prompt (caches tools + system,
        /// identical for every question) and one 5-minute breakpoint on the newest message (reused between tool rounds).
        /// </summary>
        public string BuildRequest()
        {
            var body = new Dictionary<string, object>
            {
                { "model", Model },
                { "max_tokens", MaxTokens },
                { "messages", WithMessageBreakpoint(Messages) },
            };
            if (Tools.Count > 0) body["tools"] = Tools.Select(t => (object)t.ToJson()).ToList();
            if (!string.IsNullOrEmpty(SystemPrompt))
                body["system"] = new List<object>
                {
                    new Dictionary<string, object>
                    {
                        { "type", "text" }, { "text", SystemPrompt },
                        { "cache_control", new Dictionary<string, object> { { "type", "ephemeral" }, { "ttl", "1h" } } },
                    },
                };
            return JsonWriter.Write(body);
        }

        private static List<object> WithMessageBreakpoint(List<object> messages)
        {
            if (messages.Count == 0) return messages;
            var copy = new List<object>(messages);
            var last = messages[messages.Count - 1] as Dictionary<string, object>;
            if (last == null) return copy;
            var blocks = last.TryGetValue("content", out var c) && c is List<object> l
                ? new List<object>(l)
                : new List<object> { new Dictionary<string, object> { { "type", "text" }, { "text", last.Str("content", "") } } };
            if (blocks.Count == 0 || !(blocks[blocks.Count - 1] is Dictionary<string, object> lb)) return copy;
            var marked = new Dictionary<string, object>(lb) { ["cache_control"] = new Dictionary<string, object> { { "type", "ephemeral" } } };
            blocks[blocks.Count - 1] = marked;
            var newLast = new Dictionary<string, object>(last) { ["content"] = blocks };
            copy[copy.Count - 1] = newLast;
            return copy;
        }

        /// <param name="runTool">Executes a tool (on the caller's thread) and returns the text result; throw to report an error.</param>
        /// <param name="onText">Assistant text as it arrives (one call per response block).</param>
        /// <param name="onTool">Notified before each tool runs (name, input).</param>
        public async Task RunAsync(string userText, Func<string, Dictionary<string, object>, Task<string>> runTool,
            Action<string> onText, Action<string, Dictionary<string, object>> onTool, CancellationToken ct)
        {
            Messages.Add(new Dictionary<string, object> { { "role", "user" }, { "content", userText } });
            for (int round = 0; round < MaxToolRounds; round++)
            {
                ct.ThrowIfCancellationRequested();
                string key = _apiKey();
                if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException(Lang.T("AiNoKey"));
                string json = await _transport.SendAsync(key, BuildRequest(), ct);
                var res = MiniJson.Parse(json) as Dictionary<string, object> ?? throw new InvalidOperationException("Bad response");
                Usage.Add(res.Sub("usage"));

                var content = res.List("content") ?? new List<object>();
                Messages.Add(new Dictionary<string, object> { { "role", "assistant" }, { "content", content } });

                var toolResults = new List<object>();
                foreach (var block in content.OfType<Dictionary<string, object>>())
                {
                    string type = block.Str("type");
                    if (type == "text") { var t = block.Str("text", ""); if (t.Trim().Length > 0) onText?.Invoke(t); }
                    else if (type == "tool_use")
                    {
                        string id = block.Str("id"), name = block.Str("name");
                        var input = block.Sub("input") ?? new Dictionary<string, object>();
                        onTool?.Invoke(name, input);
                        string result; bool isError = false;
                        try { result = await runTool(name, input); }
                        catch (Exception ex) { result = "ERROR: " + ex.Message; isError = true; }
                        if (result != null && result.Length > MaxToolResultChars)
                            result = result.Substring(0, MaxToolResultChars) + " …[shortened to save tokens; ask for details if needed]";
                        var tr = new Dictionary<string, object> { { "type", "tool_result" }, { "tool_use_id", id }, { "content", result ?? "" } };
                        if (isError) tr["is_error"] = true;
                        toolResults.Add(tr);
                    }
                }
                string stop = res.Str("stop_reason");
                if (toolResults.Count == 0)
                {
                    if (stop == "max_tokens") onText?.Invoke("…");
                    return;
                }
                Messages.Add(new Dictionary<string, object> { { "role", "user" }, { "content", toolResults } });
            }
            onText?.Invoke(Lang.T("AiTooManySteps"));
        }
    }
}
