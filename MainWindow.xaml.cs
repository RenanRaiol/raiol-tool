using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Windows;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using Microsoft.Win32;
using raioltool.Services;

namespace raioltool;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<LuaFileEntry> _projectFiles = [];
    private readonly LuaToolSettingsService _settingsService = new();
    private readonly LuaToolSettings _settings;
    private CancellationTokenSource? _runCancellation;
    private string? _projectDirectory;
    private string? _currentFile;
    private bool _isLoading;
    private bool _isDirty;

    public sealed record LuaFileEntry(string FullPath, string RelativePath)
    {
        public override string ToString() => RelativePath;
    }

    public MainWindow()
    {
        InitializeComponent();
        _settings = _settingsService.Load();
        ProjectFiles.ItemsSource = _projectFiles;
        Editor.Options.ConvertTabsToSpaces = true;
        Editor.Options.IndentationSize = 4;
        Editor.TextChanged += (_, _) =>
        {
            if (!_isLoading)
            {
                _isDirty = true;
                UpdateTitle();
            }
        };
        LoadLuaHighlighting();
        UpdateTitle();
    }

    private void LoadLuaHighlighting()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Syntax", "Lua.xshd");
        if (!File.Exists(path))
        {
            StatusLabel.Text = "Arquivo de highlight Lua não encontrado.";
            return;
        }

        using var reader = XmlReader.Create(path);
        Editor.SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }

    private void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscardChanges()) return;

        var dialog = new OpenFolderDialog
        {
            Title = "Selecione a pasta do projeto Lua",
            InitialDirectory = Directory.Exists(_projectDirectory)
                ? _projectDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            _projectDirectory = dialog.FolderName;
            ProjectLabel.Text = _projectDirectory;
            RefreshProjectFiles();
            StatusLabel.Text = "Projeto aberto";
            UpdateTitle();
        }
        catch (IOException ex)
        {
            ShowError("Não foi possível ler os arquivos do projeto.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            ShowError("Acesso negado ao ler o projeto.", ex);
        }
    }

    private void RefreshProjectFiles()
    {
        _projectFiles.Clear();
        if (string.IsNullOrWhiteSpace(_projectDirectory)) return;

        foreach (string file in Directory.EnumerateFiles(_projectDirectory, "*.lua", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            _projectFiles.Add(new LuaFileEntry(
                file,
                Path.GetRelativePath(_projectDirectory, file)));
        }
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscardChanges()) return;

        var dialog = new OpenFileDialog
        {
            Filter = "Arquivos Lua (*.lua)|*.lua|Todos os arquivos (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) == true)
            LoadFile(dialog.FileName);
    }

    private void NewFile_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscardChanges()) return;
        _isLoading = true;
        Editor.Clear();
        _isLoading = false;
        _currentFile = null;
        _isDirty = false;
        ProjectFiles.SelectedItem = null;
        StatusLabel.Text = "Novo arquivo Lua";
        UpdateTitle();
    }

    private void ProjectFiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProjectFiles.SelectedItem is LuaFileEntry entry)
        {
            if (!ConfirmDiscardChanges())
            {
                ProjectFiles.SelectedItem = null;
                return;
            }

            LoadFile(entry.FullPath);
        }
    }

    private void LoadFile(string path)
    {
        try
        {
            _isLoading = true;
            Editor.Text = File.ReadAllText(path);
            _currentFile = path;
            _isDirty = false;
            StatusLabel.Text = $"Aberto: {path}";
            UpdateTitle();
        }
        catch (IOException ex)
        {
            ShowError("Não foi possível abrir o arquivo.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            ShowError("Acesso negado ao abrir o arquivo.", ex);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e) => SaveDocument();

    private bool SaveDocument()
    {
        if (_currentFile is null)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Arquivo Lua (*.lua)|*.lua|Todos os arquivos (*.*)|*.*",
                DefaultExt = ".lua",
                AddExtension = true,
                FileName = "script.lua",
                InitialDirectory = Directory.Exists(_projectDirectory) ? _projectDirectory : Environment.CurrentDirectory,
            };
            if (dialog.ShowDialog(this) != true) return false;
            _currentFile = dialog.FileName;
        }

        try
        {
            File.WriteAllText(_currentFile, Editor.Text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            _isDirty = false;
            StatusLabel.Text = $"Salvo: {_currentFile}";
            UpdateTitle();
            RefreshProjectFilesIfApplicable();
            _settingsService.Save(_settings);
            return true;
        }
        catch (IOException ex)
        {
            ShowError("Não foi possível salvar o arquivo.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            ShowError("Acesso negado ao salvar o arquivo.", ex);
        }

        return false;
    }

    private void RefreshProjectFilesIfApplicable()
    {
        if (_currentFile is null || _projectDirectory is null) return;

        string relative = Path.GetRelativePath(_projectDirectory, _currentFile);
        if (!relative.StartsWith("..", StringComparison.Ordinal) && !_projectFiles.Any(f => f.FullPath == _currentFile))
            _projectFiles.Add(new LuaFileEntry(_currentFile, relative));
    }

    private bool ConfirmDiscardChanges()
    {
        if (!_isDirty) return true;

        MessageBoxResult answer = MessageBox.Show(
            this,
            "Há alterações não salvas. Deseja salvá-las antes de continuar?",
            "Alterações não salvas",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);
        if (answer == MessageBoxResult.Cancel) return false;
        return answer != MessageBoxResult.Yes || SaveDocument();
    }

    private async void Validate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            LuaValidationResult result = await LuaSyntaxValidator.ValidateAsync(
                Editor.Text,
                _settings.LuacPath,
                new ExternalToolService());
            StatusLabel.Text = result.IsValid ? "Validação aprovada" : "Erro de validação";
            AppendOutput(result.Message + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            ShowError("A validação falhou ao iniciar o luac configurado.", ex);
        }
    }

    private async void Format_Click(object sender, RoutedEventArgs e)
    {
        _settings.StyluaPath = EnsureToolConfigured(_settings.StyluaPath, "stylua.exe", "StyLua (*.exe)|*.exe");
        if (_settings.StyluaPath is null) return;

        string fileName = _currentFile ?? Path.Combine(_projectDirectory ?? Environment.CurrentDirectory, "untitled.lua");
        try
        {
            var result = await new ExternalToolService().RunAsync(
                _settings.StyluaPath!,
                ["--stdin-filepath", fileName],
                Path.GetDirectoryName(fileName),
                Editor.Text,
                CancellationToken.None);

            if (result.ExitCode != 0)
            {
                AppendOutput(string.IsNullOrWhiteSpace(result.StandardError)
                    ? $"StyLua saiu com código {result.ExitCode}."
                    : result.StandardError);
                StatusLabel.Text = "A formatação falhou; o texto não foi alterado";
                return;
            }

            _isLoading = true;
            Editor.Text = result.StandardOutput;
            _isLoading = false;
            _isDirty = true;
            StatusLabel.Text = "Formatado com StyLua; salve para manter as alterações";
            UpdateTitle();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            _isLoading = false;
            ShowError("Não foi possível executar o StyLua.", ex);
        }
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (_runCancellation is not null) return;

        _settings.LuaPath = EnsureToolConfigured(_settings.LuaPath, "lua.exe", "Lua (*.exe)|*.exe");
        if (_settings.LuaPath is null) return;
        if (!SaveDocument()) return;

        var cancellation = new CancellationTokenSource();
        _runCancellation = cancellation;
        StatusLabel.Text = "Executando Lua...";
        AppendOutput($"> \"{_settings.LuaPath}\" \"{_currentFile}\"{Environment.NewLine}");

        try
        {
            var result = await new ExternalToolService().RunAsync(
                _settings.LuaPath!,
                [_currentFile!],
                Path.GetDirectoryName(_currentFile!),
                null,
                cancellation.Token);

            if (!string.IsNullOrWhiteSpace(result.StandardOutput))
                AppendOutput(result.StandardOutput);
            if (!string.IsNullOrWhiteSpace(result.StandardError))
                AppendOutput(result.StandardError);

            if (result.ExitCode == 0)
                StatusLabel.Text = "Execução Lua concluída";
            else
                StatusLabel.Text = $"Execução Lua falhou (código {result.ExitCode})";
        }
        catch (OperationCanceledException)
        {
            AppendOutput("[execução interrompida]" + Environment.NewLine);
            StatusLabel.Text = "Execução Lua interrompida";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            ShowError("Não foi possível executar o Lua.", ex);
        }
        finally
        {
            _runCancellation?.Dispose();
            _runCancellation = null;
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _runCancellation?.Cancel();
    }

    private void ConfigureTools_Click(object sender, RoutedEventArgs e)
    {
        _settings.LuaPath = EnsureToolConfigured(_settings.LuaPath, "lua.exe", "Lua (*.exe)|*.exe");
        _settings.LuacPath = EnsureToolConfigured(_settings.LuacPath, "luac.exe", "luac (*.exe)|*.exe");
        _settings.StyluaPath = EnsureToolConfigured(_settings.StyluaPath, "stylua.exe", "StyLua (*.exe)|*.exe");

        _settingsService.Save(_settings);
        StatusLabel.Text = "Configuração de ferramentas atualizada";
        AppendOutput("Ferramentas atualizadas." + Environment.NewLine);
    }

    private void SteamInventory_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SteamInventoryReport report = SteamInventoryService.Scan();
            var builder = new StringBuilder();
            builder.AppendLine($"Diretórios Steam detectados: {report.SteamAppsDirectories.Count}");
            foreach (string directory in report.SteamAppsDirectories)
                builder.AppendLine($"- {directory}");

            builder.AppendLine();
            builder.AppendLine($"Aplicativos instalados: {report.Apps.Count}");
            foreach (SteamInstalledApp app in report.Apps.Take(50))
                builder.AppendLine($"- [{app.AppId}] {app.Name} ({app.InstallDirectory})");

            if (report.Warnings.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("Avisos:");
                foreach (string warning in report.Warnings.Take(20))
                    builder.AppendLine($"- {warning}");
            }

            StatusLabel.Text = $"Inventário Steam concluído: {report.Apps.Count} apps";
            AppendOutput(builder.ToString());
        }
        catch (Exception ex)
        {
            ShowError("Não foi possível verificar o inventário Steam.", ex);
        }
    }

    private async void BackupHash_Click(object sender, RoutedEventArgs e)
    {
        string folder = _projectDirectory ?? Environment.CurrentDirectory;
        if (string.IsNullOrWhiteSpace(_projectDirectory))
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Selecione a pasta para verificar o backup",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            };

            if (dialog.ShowDialog(this) != true) return;
            folder = dialog.FolderName;
        }

        try
        {
            var progress = new Progress<string>(message => AppendOutput(message + Environment.NewLine));
            BackupHashReport report = await BackupHashService.ScanAsync(folder, progress, CancellationToken.None);

            var builder = new StringBuilder();
            builder.AppendLine($"Raiz: {report.RootDirectory}");
            builder.AppendLine($"Arquivos calculados: {report.Files.Count}");
            foreach (BackupFileHash file in report.Files.Take(50))
                builder.AppendLine($"- {file.RelativePath} ({file.LengthBytes} bytes) -> {file.Sha256}");

            if (report.Warnings.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("Avisos:");
                foreach (string warning in report.Warnings.Take(20))
                    builder.AppendLine($"- {warning}");
            }

            StatusLabel.Text = $"Verificação de backup concluída: {report.Files.Count} arquivos";
            AppendOutput(builder.ToString());
        }
        catch (Exception ex)
        {
            ShowError("Não foi possível verificar o backup por hash.", ex);
        }
    }

    private void UpdateTitle()
    {
        string name = _currentFile is null ? "Novo arquivo" : Path.GetFileName(_currentFile);
        Title = _isDirty ? $"raiol_tool - {name}*" : $"raiol_tool - {name}";
    }

    private void AppendOutput(string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        OutputBox.Text += value;
        OutputBox.CaretIndex = OutputBox.Text.Length;
        OutputBox.ScrollToEnd();
    }

    private void ShowError(string message, Exception ex)
    {
        AppendOutput($"{message}{Environment.NewLine}{ex.Message}{Environment.NewLine}");
        MessageBox.Show(this, $"{message}{Environment.NewLine}{Environment.NewLine}{ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private string? EnsureToolConfigured(string? currentPath, string expectedName, string filter)
    {
        if (!string.IsNullOrWhiteSpace(currentPath) && File.Exists(currentPath))
            return currentPath;

        var dialog = new OpenFileDialog
        {
            Title = $"Selecione {expectedName}",
            Filter = filter,
            CheckFileExists = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        };

        if (dialog.ShowDialog(this) != true)
            return currentPath;

        string selected = dialog.FileName;
        if (string.Equals(Path.GetFileName(selected), expectedName, StringComparison.OrdinalIgnoreCase))
        {
            if (expectedName == "lua.exe") _settings.LuaPath = selected;
            else if (expectedName == "luac.exe") _settings.LuacPath = selected;
            else if (expectedName == "stylua.exe") _settings.StyluaPath = selected;
            _settingsService.Save(_settings);
            return selected;
        }

        return currentPath;
    }
}
