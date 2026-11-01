# raiol_tool

Editor desktop para desenvolvimento Lua no Windows, feito com WPF e .NET 8.

## Recursos

- Editor com realce de sintaxe, numeração de linhas e navegação de arquivos `.lua`.
- Validação completa por `luac -p` quando `luac.exe` estiver configurado. Sem ele, o app faz apenas uma checagem lexical básica; isso não substitui o parser Lua.
- Formatação por StyLua, configurando o executável `stylua.exe`.
- Execução de scripts por um `lua.exe` local, iniciada somente quando o usuário clica em **Executar Lua**.
- Caminhos dos executáveis salvos em `%AppData%\RaiolTool\settings.json`.
- Parser KeyValues/VDF somente leitura e inventário de bibliotecas Steam baseado em arquivos existentes.
- Inventário SHA-256 de backups com relatório JSON opcional.

O app não baixa nem instala runtimes. Use **Ferramentas...** para selecionar os executáveis locais `lua.exe`, `luac.exe` e `stylua.exe`.

O inventário lê manifestos existentes; não cria nem modifica arquivos `.acf`, não altera licenças ou propriedade e não integra dados de páginas Steam com processos locais. Consulte [REWADME](./REWADME) para a descrição completa da arquitetura, limites de segurança e comandos de teste.

## Compilar e executar

```powershell
dotnet build E:\raioltool\raioltool.csproj
dotnet run --project E:\raioltool\raioltool.csproj
```

O projeto é uma ferramenta de desenvolvimento Lua independente. Não altera arquivos de licença, manifestos ou dados de jogos da Steam.
