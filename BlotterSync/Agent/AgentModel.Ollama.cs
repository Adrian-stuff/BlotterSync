using System.Net.Http.Json;
using System.Text.Json;

namespace BlotterSync.Agent;

public sealed partial class AgentModel
{
    private async Task<string> RespondWithOllamaAsync(List<object> input, string instructions, JsonElement schema,
        object[] toolDefinitions, AgentTools tools, int officerId, bool isAdmin, int? recordId,
        List<AgentStep> steps, CancellationToken ct)
    {
        var endpoint = OllamaEndpoint();
        var messages = new List<object> { new { role = "system", content = instructions } };
        messages.AddRange(input);
        // Translate the shared, server-owned tool definitions to Ollama's function envelope.
        var functions = toolDefinitions.Select(definition =>
        {
            var tool = JsonSerializer.SerializeToElement(definition, AgentJson.Options);
            return new
            {
                type = "function",
                function = new
                {
                    name = tool.GetProperty("name"),
                    description = tool.GetProperty("description"),
                    parameters = tool.GetProperty("parameters")
                }
            };
        }).ToArray();

        var toolCalls = 0;
        for (var turn = 0; turn < 4; turn++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Content = JsonContent.Create(new
            {
                model = Model,
                messages,
                tools = functions,
                format = schema,
                think = false,
                stream = false,
                options = new { num_predict = 1800, num_ctx = 16384, temperature = 0 }
            });
            HttpResponseMessage response;
            try { response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct); }
            catch (HttpRequestException)
            {
                throw new AgentFailure("Could not reach Ollama. Check that the configured server is running.");
            }

            using (response)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    throw new AgentFailure("Ollama could not find the configured model. Check the model name and availability.");
                if (!response.IsSuccessStatusCode)
                    throw new AgentFailure($"Ollama returned HTTP {(int)response.StatusCode}. Check the model and server configuration.");

                using var payload = await ReadPayloadAsync(response, ct);
                var root = payload.RootElement;
                if (!root.TryGetProperty("done", out var done) || done.ValueKind != JsonValueKind.True ||
                    !root.TryGetProperty("message", out var message) ||
                    (root.TryGetProperty("done_reason", out var reason) && reason.GetString() == "length"))
                    throw new AgentFailure("Ollama did not finish its response. Try a shorter request.");

                if (!message.TryGetProperty("tool_calls", out var calls) ||
                    calls.ValueKind == JsonValueKind.Null || calls.GetArrayLength() == 0)
                    return ValidateResponseText(message.GetProperty("content").GetString());

                // Replay just the tool calls and tool results. Model thinking is neither recorded nor reused.
                messages.Add(new { role = "assistant", content = "", tool_calls = calls.Clone() });
                foreach (var call in calls.EnumerateArray())
                {
                    if (++toolCalls > 6) throw new AgentFailure("The agent reached its tool-call limit.");
                    var function = call.GetProperty("function");
                    var name = function.GetProperty("name").GetString()!;
                    var output = await tools.ExecuteAsync(name, function.GetProperty("arguments").GetRawText(),
                        officerId, isAdmin, recordId, steps, ct);
                    messages.Add(new { role = "tool", tool_name = name, content = output });
                }
            }
        }
        throw new AgentFailure("The agent reached its turn limit. Retry with a shorter request.");
    }

    private Uri OllamaEndpoint()
    {
        var address = config["AI:Ollama:BaseUrl"] ?? "http://127.0.0.1:11434";
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0 ||
            uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new AgentFailure("Set AI:Ollama:BaseUrl to an HTTP or HTTPS server URL without credentials or query parameters.");
        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/api/chat");
    }
}
