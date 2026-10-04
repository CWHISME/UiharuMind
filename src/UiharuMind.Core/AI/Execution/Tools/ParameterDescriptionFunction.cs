using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Execution.Tools;

/// <summary>
/// 改写某个参数的说明，其余一律透传。参数说明要随装配事实变（如 SendMessage 的 <c>to</c> 随收件人名单变），
/// 而 <c>[Description]</c> 只能写常量。说明在构造时定死，一次装配之内不变。
/// </summary>
internal sealed class ParameterDescriptionFunction : DelegatingAIFunction
{
    private readonly JsonElement _schema; //改写后的参数 schema

    /// <summary>
    /// 包一层，改写一个参数的说明
    /// </summary>
    /// <param name="innerFunction">原工具</param>
    /// <param name="parameterName">参数名</param>
    /// <param name="description">新的说明</param>
    public ParameterDescriptionFunction(AIFunction innerFunction, string parameterName, string description)
        : base(innerFunction)
    {
        JsonNode? schema = JsonNode.Parse(innerFunction.JsonSchema.GetRawText());
        if (schema?["properties"]?[parameterName] is not JsonObject parameter)
        {
            throw new ArgumentException($"tool '{innerFunction.Name}' has no parameter '{parameterName}'",
                nameof(parameterName));
        }

        parameter["description"] = description;
        _schema = JsonSerializer.SerializeToElement(schema);
    }

    /// <inheritdoc />
    public override JsonElement JsonSchema => _schema;
}
