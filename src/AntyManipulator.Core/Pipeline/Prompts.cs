namespace AntyManipulator.Core.Pipeline;

/// <summary>Wersjonowane prompty. Wersja trafia do zapisu przebiegu sprawdzenia.</summary>
internal static class Prompts
{
    public const string Version = "2026-11-21.2";

    public const string ExtractionSchema = """
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
            "required": ["text", "kind", "needsClarification", "clarificationQuestion", "risk", "riskReason", "queriesPl", "queriesEn"],
            "properties": {
              "text": { "type": "string" },
              "kind": { "type": "string", "enum": ["factual", "opinion", "prediction", "satire", "other"] },
              "needsClarification": { "type": "boolean" },
              "clarificationQuestion": { "type": "string" },
              "risk": { "type": "string", "enum": ["high", "normal"] },
              "riskReason": { "type": "string" },
              "queriesPl": { "type": "array", "items": { "type": "string" } },
              "queriesEn": { "type": "array", "items": { "type": "string" } }
            }
          }
        }
      }
    }
    """;

    public static string ExtractionSystem(int maxClaims, string untrustedRule) => $"""
    You are the claim-extraction component of a Polish fact-checking service for citizens.
    Your only task: read the material and list at most {maxClaims} distinct claims that a citizen may want verified.

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
    - queriesPl: 1-3 short Polish keyword queries (2-6 words, no quotes, no operators) for searching fact-checking sites and Wikipedia. queriesEn: 1-2 equivalent English queries.
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
            "required": ["passageId", "stance", "relevance", "context", "quote", "sourceRating"],
            "properties": {
              "passageId": { "type": "string" },
              "stance": { "type": "string", "enum": ["supports", "refutes", "misleading", "unrelated"] },
              "context": { "type": "boolean" },
              "relevance": { "type": "string", "enum": ["high", "medium", "low"] },
              "quote": { "type": "string" },
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
    You are the evidence-assessment component of a Polish fact-checking service. You compare ONE claim with numbered passages
    retrieved from a fixed list of sources. You do NOT decide the final verdict; it is computed separately by rules.

    For every passage return:
    - stance towards the claim:
      "supports"   = the passage says the claim is true or contains facts that confirm it;
      "refutes"    = the passage says the claim is false or contains facts that contradict it (a fact-check debunking the claim is "refutes");
      "misleading" = the passage says the claim distorts facts, lacks key context, mixes true and false elements, or a fact-checker rated it as manipulation or misleading;
      "unrelated"  = the passage is about something else or only shares keywords.
    - relevance: "high" = the same event or fact as the claim, "medium" = closely related, "low" = tangential.
    - context: true when the passage is "unrelated" to the claim itself but gives useful background about the same event, people or topic
      (for example the investigation, dispute or situation the claim refers to). Otherwise false. Context never confirms or refutes anything.
    - quote: copy VERBATIM (exact characters, no changes) the shortest fragment of that passage, at most 300 characters, that justifies the stance,
      or for a context passage the fragment that describes the background. "" if unrelated and not context. Never paraphrase in this field.
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
