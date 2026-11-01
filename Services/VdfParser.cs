using System.Text;

namespace raioltool.Services;

public sealed record VdfEntry(string Key, string? Value, IReadOnlyList<VdfEntry>? Children)
{
    public bool IsObject => Children is not null;

    public VdfEntry? Find(string key) =>
        Children?.FirstOrDefault(entry => string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase));

    public bool TryGetValue(string key, out string value)
    {
        VdfEntry? entry = Find(key);
        if (entry is { IsObject: false, Value: not null })
        {
            value = entry.Value;
            return true;
        }

        value = string.Empty;
        return false;
    }

    public IEnumerable<VdfEntry> FindAll(string key) =>
        Children?.Where(entry => string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
        ?? Enumerable.Empty<VdfEntry>();
}

public sealed class VdfParseException(string message, int line, int column)
    : FormatException($"{message} (linha {line}, coluna {column}).")
{
    public int Line { get; } = line;
    public int Column { get; } = column;
}

public static class VdfParser
{
    public static IReadOnlyList<VdfEntry> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var parser = new Parser(text);
        return parser.ParseDocument();
    }

    private sealed class Parser(string text)
    {
        private int _position;
        private int _line = 1;
        private int _column = 1;

        public IReadOnlyList<VdfEntry> ParseDocument()
        {
            List<VdfEntry> entries = ParseEntries(expectCloseBrace: false);
            SkipTrivia();
            if (_position < text.Length)
                throw Error("Conteúdo inesperado após o documento");
            return entries;
        }

        private List<VdfEntry> ParseEntries(bool expectCloseBrace)
        {
            var entries = new List<VdfEntry>();
            while (true)
            {
                SkipTrivia();
                if (_position >= text.Length)
                {
                    if (expectCloseBrace) throw Error("Bloco VDF não foi fechado");
                    return entries;
                }

                if (Peek() == '}')
                {
                    if (!expectCloseBrace) throw Error("Chave de fechamento sem bloco correspondente");
                    Advance();
                    return entries;
                }

                string key = ReadStringToken("Esperava uma chave");
                SkipTrivia();
                if (_position >= text.Length) throw Error($"Falta um valor para a chave '{key}'");

                if (Peek() == '{')
                {
                    Advance();
                    entries.Add(new VdfEntry(key, null, ParseEntries(expectCloseBrace: true)));
                }
                else
                {
                    string value = ReadStringToken($"Esperava um valor para a chave '{key}'");
                    entries.Add(new VdfEntry(key, value, null));
                }
            }
        }

        private string ReadStringToken(string expectation)
        {
            SkipTrivia();
            if (_position >= text.Length || Peek() is '{' or '}')
                throw Error(expectation);

            if (Peek() != '"')
            {
                int start = _position;
                while (_position < text.Length && !char.IsWhiteSpace(Peek()) && Peek() is not '{' and not '}')
                    Advance();
                if (_position == start) throw Error(expectation);
                return text[start.._position];
            }

            Advance();
            var value = new StringBuilder();
            while (_position < text.Length)
            {
                char current = Peek();
                Advance();

                if (current == '"') return value.ToString();
                if (current == '\\' && _position < text.Length)
                {
                    char escaped = Peek();
                    Advance();
                    value.Append(escaped is '"' or '\\' ? escaped : '\\');
                    if (escaped is not '"' and not '\\') value.Append(escaped);
                    continue;
                }

                value.Append(current);
            }

            throw Error("String entre aspas não foi fechada");
        }

        private void SkipTrivia()
        {
            while (_position < text.Length)
            {
                if (char.IsWhiteSpace(Peek()))
                {
                    Advance();
                    continue;
                }

                if (Peek() == '/' && _position + 1 < text.Length && text[_position + 1] == '/')
                {
                    while (_position < text.Length && Peek() != '\n')
                        Advance();
                    continue;
                }

                break;
            }
        }

        private char Peek() => text[_position];

        private void Advance()
        {
            if (text[_position++] == '\n')
            {
                _line++;
                _column = 1;
            }
            else
            {
                _column++;
            }
        }

        private VdfParseException Error(string message) => new(message, _line, _column);
    }
}
