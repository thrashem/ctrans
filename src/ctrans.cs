/*
 * ctrans - クリップボードのテキストを翻訳してクリップボードに返す
 *
 * Copyright (c) 2025 thrashem
 * Released under the MIT License
 *
 * Build:
 *   C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe ^
 *     /r:System.Windows.Forms.dll ^
 *     /out:ctrans.exe ctrans.cs
 *
 * Usage:
 *   ctrans              翻訳先: 日本語（デフォルト）
 *   ctrans ja           翻訳先: 日本語
 *   ctrans en           翻訳先: 英語
 *   ctrans zh-CN        翻訳先: 中国語（簡体字）など任意の言語コード
 *   ctrans -d           デバッグ: APIレスポンスを標準エラーに出力
 */

using System;
using System.Net;
using System.Text;
using System.Windows.Forms;

class Program {
    [STAThread]
    static int Main(string[] args) {
        if (args.Length > 0 && (args[0] == "-h" || args[0] == "--help" || args[0] == "/?")) {
            Console.WriteLine("ctrans - Translate clipboard text and return to clipboard");
            Console.WriteLine("Copyright (c) 2025 thrashem\n");
            Console.WriteLine("Usage:");
            Console.WriteLine("  ctrans [lang] [-d]");
            Console.WriteLine("  ctrans -h, --help, /?  Show this help\n");
            Console.WriteLine("Options:");
            Console.WriteLine("  lang   Target language code (default: ja)");
            Console.WriteLine("  -d     Output raw API response to stderr\n");
            Console.WriteLine("Examples:");
            Console.WriteLine("  ctrans              Translate to Japanese");
            Console.WriteLine("  ctrans en           Translate to English");
            Console.WriteLine("  ctrans zh-CN        Translate to Chinese (Simplified)");
            Console.WriteLine("  ctrans -d           Translate to Japanese with debug output");
            Console.WriteLine("  ctrans en -d 2> debug.txt  Save API response to file");
            return 0;
        }

        bool   debug = false;
        string tl    = "ja";
        foreach (string arg in args) {
            if (arg == "-d" || arg == "--debug") debug = true;
            else if (arg.Length > 0)             tl    = arg;
        }

        /* クリップボードからテキストを取得する
         * STAThread が必要。Clipboard クラスは STA スレッドでのみ動作する。
         */
        string input = null;
        try {
            input = Clipboard.GetText();
        } catch (Exception e) {
            Console.Error.WriteLine("Error: Cannot read clipboard: " + e.Message);
            return 1;
        }

        if (string.IsNullOrEmpty(input)) {
            Console.Error.WriteLine("Error: No text in clipboard");
            return 2;
        }

        /* Google翻訳 非公式APIを呼び出す
         * WebClient はデフォルトで UTF-8 レスポンスを正しく扱う。
         */
        string translated = null;
        try {
            string url = string.Format(
                "https://translate.googleapis.com/translate_a/single" +
                "?client=gtx&sl=auto&tl={0}&dt=t&q={1}",
                Uri.EscapeDataString(tl),
                Uri.EscapeDataString(input)
            );

            using (WebClient wc = new WebClient()) {
                wc.Encoding = Encoding.UTF8;
                string json = wc.DownloadString(url);
                if (debug) Console.Error.WriteLine(json);
                translated = ExtractTranslation(json);
            }
        } catch (Exception e) {
            Console.Error.WriteLine("Error: Translation failed: " + e.Message);
            return 3;
        }

        if (string.IsNullOrEmpty(translated)) {
            Console.Error.WriteLine("Error: Empty translation result");
            return 4;
        }

        /* \r\n 正規化: \n 単体を Windows クリップボードの標準改行 \r\n に揃える */
        translated = translated.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");

        /* 翻訳結果をクリップボードに書き込む
         * SetText は CF_UNICODETEXT として登録するため文字化けしない。
         */
        try {
            Clipboard.SetText(translated);
        } catch (Exception e) {
            Console.Error.WriteLine("Error: Cannot write clipboard: " + e.Message);
            return 5;
        }

        return 0;
    }

    /*
     * Google翻訳APIのJSONレスポンスから翻訳テキストを抽出する。
     *
     * レスポンス構造（深さ対応）:
     *   [                    depth=1 最外配列
     *    [                   depth=2 チャンク列
     *     "翻訳1",                   depth=2 直下の先頭文字列 = 翻訳結果 ← 取得
     *     "原文1", null, ...         depth=2 直下の後続要素  = スキップ
     *     [[]],                      depth=3 以降はスキップ
     *     [[["hash","model"]]]       depth=3,4,5
     *    ],                  depth=1 チャンク終了 → needTranslation=true にリセット
     *    [                   depth=2 次のチャンク
     *     "翻訳2", ...               同様
     *    ],
     *    ...
     *   ]                    depth=0 全終了
     *
     * アルゴリズム:
     *   "[[[" の直後から depth=2 で開始。
     *   depth==2 かつ needTranslation==true のとき最初の '"' を翻訳結果として取得。
     *   ']' で depth==1 になったらチャンク終了、needTranslation をリセット。
     *   depth==0 で終了。
     */
    static string ExtractTranslation(string json) {
        StringBuilder sb  = new StringBuilder();
        int           len = json.Length;

        int start = json.IndexOf("[[[");
        if (start < 0) return null;

        int  i               = start + 3;
        int  depth           = 2;
        bool needTranslation = true;

        while (i < len) {
            char c = json[i];

            if (c == '[') {
                depth++;
                i++;
            } else if (c == ']') {
                depth--;
                i++;
                if (depth == 0) break;               /* 全チャンク終了 */
                if (depth == 1) needTranslation = true; /* チャンク終了→次へ */
            } else if (c == '"') {
                i++;
                if (depth == 2 && needTranslation) {
                    sb.Append(ReadString(json, ref i, len));
                    needTranslation = false;
                } else {
                    SkipString(json, ref i, len);
                }
            } else {
                i++;
            }
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    /*
     * 現在位置から JSON 文字列を読み取り、i を閉じ '"' の次に進める。
     * エスケープシーケンスを処理して文字列を返す。
     */
    static string ReadString(string json, ref int i, int len) {
        StringBuilder sb = new StringBuilder();
        while (i < len) {
            char c = json[i];
            if (c == '\\' && i + 1 < len) {
                char next = json[i + 1];
                switch (next) {
                    case 'n':  sb.Append('\n'); i += 2; break;
                    case 'r':  sb.Append('\r'); i += 2; break;
                    case 't':  sb.Append('\t'); i += 2; break;
                    case '"':  sb.Append('"');  i += 2; break;
                    case '\\': sb.Append('\\'); i += 2; break;
                    case 'u':
                        if (i + 5 < len) {
                            string hex = json.Substring(i + 2, 4);
                            try { sb.Append((char)Convert.ToInt32(hex, 16)); }
                            catch { sb.Append('?'); }
                            i += 6;
                        } else { i++; }
                        break;
                    default:
                        sb.Append(next);
                        i += 2;
                        break;
                }
            } else if (c == '"') {
                i++;
                break;
            } else {
                sb.Append(c);
                i++;
            }
        }
        return sb.ToString();
    }

    /*
     * 現在位置から JSON 文字列を読み飛ばし、i を閉じ '"' の次に進める。
     */
    static void SkipString(string json, ref int i, int len) {
        while (i < len) {
            char c = json[i];
            if (c == '\\') { i += 2; continue; }
            if (c == '"')  { i++; break; }
            i++;
        }
    }
}