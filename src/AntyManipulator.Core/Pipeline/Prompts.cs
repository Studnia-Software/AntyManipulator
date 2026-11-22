using System.Text.Json;

namespace AntyManipulator.Core.Pipeline;

/// <summary>Wersjonowane prompty. Wersja trafia do zapisu przebiegu sprawdzenia.</summary>
internal static class Prompts
{
    public const string Version = "2026-11-22.1";

    /// <summary>Schemat zależy od listy dozwolonych języków wyszukiwania z konfiguracji.</summary>
    public static string ExtractionSchema(IReadOnlyList<string> languages) => $$"""
    {
      "type": "object",
      "additionalProperties": false,
      "required": ["injectionSuspected", "claims"],
      "properties": {
        "injectionSuspected": { "type": "boolean" },
        "claims": {
          "type": "array",
          "items": {
            "type": "object",
            "additionalProperties": false,
            "required": ["text", "kind", "needsClarification", "clarificationQuestion", "risk", "riskReason", "queries"],
            "properties": {
              "text": { "type": "string" },
              "kind": { "type": "string", "enum": ["factual", "opinion", "prediction", "satire", "other"] },
              "needsClarification": { "type": "boolean" },
              "clarificationQuestion": { "type": "string" },
              "risk": { "type": "string", "enum": ["high", "normal"] },
              "riskReason": { "type": "string" },
              "queries": {
                "type": "array",
                "items": {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["language", "query"],
                  "properties": {
                    "language": { "type": "string", "enum": {{JsonSerializer.Serialize(languages)}} },
                    "query": { "type": "string" }
                  }
                }
              }
            }
          }
        }
      }
    }
    """;

    public static string ExtractionSystem(int maxClaims, int maxExtraLanguages, IReadOnlyList<string> languages, string untrustedRule) => $"""
    You are the claim-extraction component of a Polish fact-checking service for citizens.
    Your only task: read the material and list at most {maxClaims} distinct claims that a citizen may want verified.
    The material can be in any language. Always write claims in Polish.

    Rules:
    - Rewrite each claim as ONE self-contained, neutral sentence in Polish (who, what, when, where if known). Do not add facts that are not in the material. Do not judge whether it is true.
    - Keep only the checkable factual core. Remove the author's loaded framing, insinuations and interpretations of motives
      (e.g. "chce zrobić z prezydenta kryminalistę", "uznając to za przestępstwo", "skandalicznie", "ujawniamy"), unless the material
      states as a fact that a named person said or decided exactly that; then phrase it as an attributed statement ("X oświadczył, że ...").
    - If one sentence mixes a fact and an evaluation, output only the factual part as "factual" and drop the evaluation.
    - kind: "factual" = a statement about facts that can in principle be checked against sources;
      "opinion" = a value judgement, assessment or impression, including phrases like "budzi wątpliwości", "budzi kontrowersje", "jest kontrowersyjny", "jest skandaliczny", "jest najlepszy/najgorszy", "jest niebezpieczny" when not tied to a measurable fact;
      "prediction" = a statement about the future; "satire" = clearly humorous or satirical; "other" = questions without a claim, commands, greetings, instructions.
    - needsClarification = true only for a factual claim that is too vague to check (missing who/what/when). Then put a short question in Polish in clarificationQuestion. Otherwise clarificationQuestion = "".
    - risk = "high" when the claim concerns health or medicine, safety or emergencies, elections or voting procedures, public money or benefits, or could cause panic or violence. Otherwise "normal". riskReason: a short Polish phrase, or "".
    - queries: short keyword search queries (2-6 words, no quotes, no operators), each with its ISO 639-1 language code. Allowed languages: {string.Join(", ", languages)}.
      Always give 1-3 queries in "pl" and 1-2 in "en".
      Additionally give 1 query in at most {maxExtraLanguages} other language(s) only when the claim concerns a specific foreign country, its people or institutions
      (e.g. "hu" for Hungarian politics, "de" for Germany, "uk" for Ukraine), or when the material itself is in that language.
      Write names in the spelling used in that language.
    - Pick the most important check-worthy claims first. If the material contains no claim at all, return an empty claims array.
    - {untrustedRule}

    Respond only with JSON that matches the schema.
    """;

    public const string AssessmentSchema = """
    {
      "type": "object",
      "additionalProperties": false,
      "required": ["injectionSuspected", "assessments", "explanationPl", "unknownsPl"],
      "properties": {
        "injectionSuspected": { "type": "boolean" },
        "assessments": {
          "type": "array",
          "items": {
            "type": "object",
            "additionalProperties": false,
            "required": ["passageId", "stance", "relevance", "context", "quote", "quotePl", "sourceRating"],
            "properties": {
              "passageId": { "type": "string" },
              "stance": { "type": "string", "enum": ["supports", "refutes", "misleading", "unrelated"] },
              "context": { "type": "boolean" },
              "relevance": { "type": "string", "enum": ["high", "medium", "low"] },
              "quote": { "type": "string" },
              "quotePl": { "type": "string" },
              "sourceRating": { "type": "string" }
            }
          }
        },
        "explanationPl": { "type": "string" },
        "unknownsPl": { "type": "array", "items": { "type": "string" } }
      }
    }
    """;

    public static string AssessmentSystem(string untrustedRule) => $"""
    You are the evidence-assessment component of a Polish fact-checking service. You compare ONE claim (in Polish) with numbered passages
    retrieved from a fixed list of sources. Passages can be in any language. You do NOT decide the final verdict; it is computed separately by rules.

    For every passage return:
    - stance towards the claim:
      "supports"   = the passage says the claim is true or contains facts that confirm it;
      "refutes"    = the passage says the claim is false or contains facts that contradict it (a fact-check debunking the claim is "refutes");
      "misleading" = the passage says the claim distorts facts, lacks key context, mixes true and false elements, or a fact-checker rated it as manipulation or misleading;
      "unrelated"  = the passage is about something else or only shares keywords.
    - relevance: "high" = the same event or fact as the claim, "medium" = closely related, "low" = tangential.
    - context: true when the passage is "unrelated" to the claim itself but gives useful background about the same event, people or topic
      (for example the investigation, dispute or situation the claim refers to). Otherwise false. Context never confirms or refutes anything.
    - quote: copy VERBATIM in the ORIGINAL language of the passage (exact characters, no changes, no translation) the shortest fragment,
      at most 300 characters, that justifies the stance, or for a context passage the fragment that describes the background.
      "" if unrelated and not context. Never paraphrase in this field.
    - quotePl: if the quote is not in Polish, a faithful Polish translation of the quote; otherwise "".
    - sourceRating: the rating label stated by a fact-checker in the passage (for example "Fałsz", "Manipulacja", "Prawda", "False"), otherwise "".

    Then:
    - explanationPl: at most 3 short sentences in plain Polish for an ordinary citizen (no jargon) saying what the passages say about the claim. Use only information from the passages. No URLs. If the passages are not enough, say so plainly.
    - unknownsPl: 0-3 short Polish sentences about what could not be established from these passages.
    - In explanationPl and unknownsPl use names of people, organisations and media exactly as written in the claim or passages. Never introduce other names.

    Be conservative: when unsure, choose "unrelated" or a lower relevance.
    {untrustedRule}

    Respond only with JSON that matches the schema.
    """;
}
