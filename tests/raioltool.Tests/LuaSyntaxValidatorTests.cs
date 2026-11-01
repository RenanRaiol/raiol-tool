using raioltool.Services;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using System.Xml;

namespace raioltool.Tests;

public class LuaSyntaxValidatorTests
{
    [Fact]
    public void LuaHighlightingDefinition_Loads()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Syntax", "Lua.xshd");
        using XmlReader reader = XmlReader.Create(path);

        var definition = HighlightingLoader.Load(reader, HighlightingManager.Instance);

        Assert.Equal("Lua", definition.Name);
    }

    [Fact]
    public async Task ValidateAsync_IgnoresDelimitersInsideStringsAndComments()
    {
        const string source = """
            --[=[ unmatched { ) ]=]
            local quoted = "{ not a delimiter }"
            local longText = [==[ ( [ { } ) ]==]
            return { quoted = quoted, text = longText }
            """;

        LuaValidationResult result = await LuaSyntaxValidator.ValidateAsync(
            source, null, new ExternalToolService());

        Assert.True(result.IsValid);
        Assert.Contains("Checagem lexical básica", result.Message);
    }

    [Theory]
    [InlineData("return {]", "Delimitador")]
    [InlineData("local value = 'unterminated", "String não terminada")]
    [InlineData("--[=[ unterminated", "Comentário longo não terminado")]
    [InlineData("local value = [==[ unterminated", "String longa não terminada")]
    public async Task ValidateAsync_ReportsUnbalancedLexicalConstructs(string source, string expectedMessage)
    {
        LuaValidationResult result = await LuaSyntaxValidator.ValidateAsync(
            source, null, new ExternalToolService());

        Assert.False(result.IsValid);
        Assert.Contains(expectedMessage, result.Message);
    }
}
