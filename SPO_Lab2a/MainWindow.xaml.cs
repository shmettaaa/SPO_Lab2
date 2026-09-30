using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;

namespace SPO_Lab2a
{
    public partial class MainWindow : Window
    {
        public MainWindow() { InitializeComponent(); }

        private void AnalyzeButton_Click(object sender, RoutedEventArgs e)
        {
            var code = CodeTextBox.Text;
            if (string.IsNullOrWhiteSpace(code))
            {
                MessageBox.Show("Введите код для анализа.", "Внимание");
                return;
            }

            try
            {
                var result = GilbAnalyzer.Analyze(code);
                ResultCL.Text = $"Абсолютная сложность (CL): {result.CL}";
                ResultCl.Text = $"Относительная сложность (cl): {result.Cl:F3}";
                ResultCLI.Text = $"Макс. уровень вложенности (CLI): {result.CLI}";
                ResultTotal.Text = $"Общее число операторов: {result.TotalStatements}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка анализа: {ex.Message}", "Ошибка");
            }
        }

        private void LoadFileButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "C++ files (*.cpp;*.h;*.hpp)|*.cpp;*.h;*.hpp|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true)
                CodeTextBox.Text = File.ReadAllText(dlg.FileName);
        }
    }

    public class GilbResult
    {
        public int CL { get; set; }
        public double Cl { get; set; }
        public int CLI { get; set; }
        public int TotalStatements { get; set; }
    }

    public static class GilbAnalyzer
    {
        public static GilbResult Analyze(string sourceCode)
        {
            string cleaned = Lexer.CleanCode(sourceCode);
            var tokens = Lexer.Tokenize(cleaned);

            var counter = new GilbCounter();
            counter.Process(tokens);

            int cl = counter.CL;
            int total = counter.TotalStatements;
            int cli = counter.CLI;
            double clRelative = total > 0 ? (double)cl / total : 0.0;

            return new GilbResult
            {
                CL = cl,
                Cl = clRelative,
                CLI = cli,
                TotalStatements = total
            };
        }
    }

    // =================================================================
    internal static class Lexer
    {
        public static string CleanCode(string code)
        {
            var sb = new StringBuilder(code.Length);
            int i = 0, n = code.Length;
            while (i < n)
            {
                char c = code[i];

                if (c == '"')
                {
                    sb.Append("\"\"");
                    i++;
                    while (i < n)
                    {
                        if (code[i] == '\\' && i + 1 < n) { i += 2; continue; }
                        if (code[i] == '"') { i++; break; }
                        i++;
                    }
                    continue;
                }

                if (c == '\'')
                {
                    sb.Append("''");
                    i++;
                    while (i < n)
                    {
                        if (code[i] == '\\' && i + 1 < n) { i += 2; continue; }
                        if (code[i] == '\'') { i++; break; }
                        i++;
                    }
                    continue;
                }

                if (c == '/' && i + 1 < n && code[i + 1] == '/')
                {
                    i += 2;
                    while (i < n && code[i] != '\n') i++;
                    continue;
                }

                if (c == '/' && i + 1 < n && code[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < n && !(code[i] == '*' && code[i + 1] == '/')) i++;
                    i += 2;
                    continue;
                }

                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        public static List<string> Tokenize(string code)
        {
            var tokens = new List<string>();
            int i = 0, n = code.Length;
            while (i < n)
            {
                char c = code[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }

                if (char.IsLetter(c) || c == '_')
                {
                    int start = i;
                    while (i < n && (char.IsLetterOrDigit(code[i]) || code[i] == '_')) i++;
                    tokens.Add(code.Substring(start, i - start));
                    continue;
                }

                if (c == '{' || c == '}' || c == ';' || c == ':' || c == '(' || c == ')')
                {
                    tokens.Add(c.ToString());
                    i++;
                    continue;
                }

                i++;
            }
            return tokens;
        }
    }

    // =================================================================
    // Счётчик метрик Джилба
    //
    //   CL: if / for / while / do / case → +1
    //       default → игнор
    //
    //   CLI: уровень ветвления = число охватывающих его ветвлений.
    //        switch — не ветвление, сам по себе уровня не даёт.
    //        case k в switch с базой B → уровень = B + (k-1) + (B > 0 ? 1 : 0)
    //        (если switch вложен, case 1 вложен как обычный if — на уровень глубже)
    //        default → игнор
    // =================================================================
    internal class GilbCounter
    {
        public int CL { get; private set; }
        public int TotalStatements { get; private set; }
        public int CLI { get; private set; }

        private int _depth = 0;

        private readonly Stack<BlockInfo> _blockStack = new Stack<BlockInfo>();
        private PendingKind _pending = PendingKind.None;

        private enum PendingKind { None, Control, Switch, DoBody }
        private enum BlockKind { Normal, Control, Switch, DoBody }

        private class BlockInfo
        {
            public BlockKind Kind;
            public int SavedDepth;
            public int SwitchCaseIndex;
            public int SwitchBaseDepth;
        }

        public void Process(List<string> tokens)
        {
            for (int i = 0; i < tokens.Count; i++)
            {
                string token = tokens[i];

                switch (token)
                {
                    case ";":
                        TotalStatements++;
                        if (_pending == PendingKind.Control) _pending = PendingKind.None;
                        break;

                    case "{": HandleOpenBrace(); break;
                    case "}": HandleCloseBrace(); break;

                    case "if":
                    case "for":
                        CL++;
                        TotalStatements++;
                        if (_depth > CLI) CLI = _depth;
                        _pending = PendingKind.Control;
                        break;

                    case "while":
                        if (IsDoWhile(tokens, i))
                        {
                            _pending = PendingKind.None;
                        }
                        else
                        {
                            CL++;
                            TotalStatements++;
                            if (_depth > CLI) CLI = _depth;
                            _pending = PendingKind.Control;
                        }
                        break;

                    case "do":
                        CL++;
                        TotalStatements++;
                        if (_depth > CLI) CLI = _depth;
                        _pending = PendingKind.DoBody;
                        break;

                    case "switch":
                        TotalStatements++;
                        _pending = PendingKind.Switch;
                        break;

                    case "case":
                        HandleCase();
                        break;

                    case "default":
                        // не учитывается нигде
                        break;

                    case "else":
                        // else — часть того же if, отдельного уровня не даёт.
                        // Если после else идёт if — обработается как обычный if.
                        _pending = PendingKind.Control;
                        break;
                }
            }
        }

        private void HandleOpenBrace()
        {
            var info = new BlockInfo { SavedDepth = _depth };

            switch (_pending)
            {
                case PendingKind.Control:
                    _depth++;
                    info.Kind = BlockKind.Control;
                    break;

                case PendingKind.Switch:
                    // switch НЕ повышает _depth.
                    info.Kind = BlockKind.Switch;
                    info.SwitchCaseIndex = 0;
                    info.SwitchBaseDepth = _depth;
                    break;

                case PendingKind.DoBody:
                    _depth++;
                    info.Kind = BlockKind.DoBody;
                    break;

                default:
                    info.Kind = BlockKind.Normal;
                    break;
            }

            _pending = PendingKind.None;
            _blockStack.Push(info);
        }

        private void HandleCloseBrace()
        {
            if (_blockStack.Count == 0) return;
            var info = _blockStack.Pop();

            if (info.Kind == BlockKind.Switch)
            {
                // switch с n case = n if
                CL += info.SwitchCaseIndex;
            }

            _depth = info.SavedDepth;
        }

        private void HandleCase()
        {
            if (_blockStack.Count == 0) return;
            var top = _blockStack.Peek();
            if (top.Kind != BlockKind.Switch) return;

            int baseDepth = top.SwitchBaseDepth;
            int offset = (baseDepth > 0) ? 1 : 0;

            // case k (нумерация с 0): уровень = baseDepth + k + offset
            int caseNesting = baseDepth + top.SwitchCaseIndex + offset;
            top.SwitchCaseIndex++;

            if (caseNesting > CLI) CLI = caseNesting;

            // тело case — на уровень глубже самого case
            _depth = caseNesting + 1;
        }

        private bool IsDoWhile(List<string> tokens, int whileIndex)
        {
            int j = whileIndex - 1;
            if (j < 0 || tokens[j] != "}") return false;

            int depth = 0;
            for (int k = j; k >= 0; k--)
            {
                if (tokens[k] == "}") depth++;
                else if (tokens[k] == "{")
                {
                    depth--;
                    if (depth == 0)
                        return k >= 1 && tokens[k - 1] == "do";
                }
            }
            return false;
        }
    }
}