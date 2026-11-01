using System.IO;

namespace raioltool.Services;

public sealed record LuaValidationResult(bool IsValid, string Message);

public static class LuaSyntaxValidator
{
    public static async Task<LuaValidationResult> ValidateAsync(
        string source,
        string? luacPath,
        ExternalToolService processService,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(luacPath))
            return CheckLexicalStructure(source);
        if (!File.Exists(luacPath))
            return new LuaValidationResult(false, $"luac.exe configurado não foi encontrado: {luacPath}");

        string tempPath = Path.Combine(Path.GetTempPath(), $"raioltool-{Guid.NewGuid():N}.lua");
        await File.WriteAllTextAsync(tempPath, source, cancellationToken);
        try
        {
            ExternalToolResult result = await processService.RunAsync(
                luacPath,
                ["-p", tempPath],
                Path.GetTempPath(),
                null,
                cancellationToken);
            if (result.ExitCode == 0)
                return new LuaValidationResult(true, "Sintaxe Lua válida (luac -p).");

            string diagnostic = string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardOutput
                : result.StandardError;
            return new LuaValidationResult(false, string.IsNullOrWhiteSpace(diagnostic)
                ? $"luac terminou com código {result.ExitCode}."
                : diagnostic.Trim());
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    private static LuaValidationResult CheckLexicalStructure(string source)
    {
        var delimiters = new Stack<(char Character, int Line)>();
        int line = 1;
        for (int i = 0; i < source.Length; i++)
        {
            char current = source[i];

            if (current == '\n')
            {
                line++;
                continue;
            }

            if (current == '-' && i + 1 < source.Length && source[i + 1] == '-')
            {
                i += 2;
                if (TryReadLongBracket(source, i, out string close, out int contentStart))
                {
                    int end = source.IndexOf(close, contentStart, StringComparison.Ordinal);
                    if (end < 0)
                        return Invalid("Comentário longo não terminado", line);
                    line += CountLines(source, i, end + close.Length);
                    i = end + close.Length - 1;
                }
                else
                {
                    while (i < source.Length && source[i] != '\n') i++;
                    if (i < source.Length) line++;
                }
                continue;
            }

            if (current is '\'' or '"')
            {
                char quote = current;
                int startLine = line;
                bool closed = false;
                for (i++; i < source.Length; i++)
                {
                    if (source[i] == '\\')
                    {
                        if (i + 1 < source.Length)
                        {
                            if (source[i + 1] == '\n') line++;
                            i++;
                            continue;
                        }
                    }
                    else if (source[i] == quote)
                    {
                        closed = true;
                        break;
                    }
                    else if (source[i] == '\n')
                    {
                        return Invalid("String curta contém quebra de linha", line);
                    }
                }

                if (!closed) return Invalid("String não terminada", startLine);
                continue;
            }

            if (current == '[' && TryReadLongBracket(source, i, out string longClose, out int longStart))
            {
                int end = source.IndexOf(longClose, longStart, StringComparison.Ordinal);
                if (end < 0) return Invalid("String longa não terminada", line);
                line += CountLines(source, i, end + longClose.Length);
                i = end + longClose.Length - 1;
                continue;
            }

            if (current is '(' or '[' or '{')
            {
                delimiters.Push((current, line));
                continue;
            }

            if (current is ')' or ']' or '}')
            {
                if (delimiters.Count == 0 || !Matches(delimiters.Peek().Character, current))
                    return Invalid($"Delimitador '{current}' sem abertura correspondente", line);
                delimiters.Pop();
            }
        }

        if (delimiters.TryPeek(out var unclosed))
            return Invalid($"Delimitador '{unclosed.Character}' aberto aqui não foi fechado", unclosed.Line);

        return new LuaValidationResult(
            true,
            "Checagem lexical básica passou (strings, comentários e delimitadores). Configure luac.exe para validar a gramática Lua completa.");
    }

    private static bool TryReadLongBracket(string source, int index, out string closing, out int contentStart)
    {
        closing = string.Empty;
        contentStart = index;
        if (index >= source.Length || source[index] != '[') return false;

        int cursor = index + 1;
        while (cursor < source.Length && source[cursor] == '=') cursor++;
        if (cursor >= source.Length || source[cursor] != '[') return false;

        string equals = source[(index + 1)..cursor];
        closing = $"]{equals}]";
        contentStart = cursor + 1;
        return true;
    }

    private static bool Matches(char opening, char closing) =>
        (opening, closing) is ('(', ')') or ('[', ']') or ('{', '}');

    private static int CountLines(string source, int start, int end)
    {
        int count = 0;
        for (int i = start; i < end; i++)
            if (source[i] == '\n') count++;
        return count;
    }

    private static LuaValidationResult Invalid(string message, int line) =>
        new(false, $"{message} (linha {line}).");
}
