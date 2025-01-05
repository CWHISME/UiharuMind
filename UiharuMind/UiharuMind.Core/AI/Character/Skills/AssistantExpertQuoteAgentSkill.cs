namespace UiharuMind.Core.AI.Character.Skills;

public class AssistantExpertQuoteAgentSkill : NormalAgentSkill
{
    public AssistantExpertQuoteAgentSkill(string quoteStr) : base(DefaultCharacter.AssistantExpertQuote)
    {
        SetParams("quote", quoteStr);
    }
}