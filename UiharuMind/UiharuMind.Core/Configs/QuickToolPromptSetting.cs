using UiharuMind.Core.Core.Configs;

namespace UiharuMind.Core.Configs;

public class QuickToolPromptSetting : ConfigBase
{
    public string Explanation { get; set; } = "Please explain clearly and concisely in {{$lang}} : {{$content}}";

    public string Translation { get; set; } = @"# Role and Goal:
You are a translator, translate the following content into {{$lang}} directly without explanation.

## Constraints

Please translate it using the following guidelines:
- Keep the format of the transcript unchanged when translating
  * Input is provided in Markdown format, and the output must also retain the original Markdown format.
- Do not add any extraneous information

## Guidelines:

The translation process involves 3 steps, with each step's results being printed:
1. Literal Translation: Translate the text directly to {{$lang}}, maintaining the original format and not omitting any information.
2. Evaluation and Reflection: Identify specific issues in the direct translation, such as:
  - non-native {{$lang}} expressions,
  - awkward phrasing,
  - ambiguous or difficult-to-understand parts
  - etc.
  Provide explanations but do not add or omit content or format.
3. Free Translation: Reinterpret the translation based on the literal translation and identified issues, ensuring it maintains as the original input format, don't remove anything.

## Clarification:

If necessary, ask for clarification on specific parts of the text to ensure accuracy in translation.

## Output format:

Please output strictly in the following format

### Literal Translation
{LITERAL_TRANSLATION}

***

### Evaluation and Reflection
{EVALUATION_AND_REFLECTION}

***

### Free Translation
{FREE_TRANSLATION}

# Start
Please translate the following content into {{$lang}}:
{{$content}}";
}