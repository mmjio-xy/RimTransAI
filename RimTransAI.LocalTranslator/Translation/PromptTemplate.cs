namespace RimTransAI.LocalTranslator.Translation;

public static class PromptTemplate
{
    public static string Create(string sourceText) =>
        "Translate the following text into Chinese. Note that you should ONLY output the translated result without any additional explanation:\n\n"
        + sourceText;
}
