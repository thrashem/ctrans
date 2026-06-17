# ctrans - クリップボードのテキストを翻訳してクリップボードに返す
Copyright (c) 2025 thrashem
Released under the MIT License

Build:
  C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /r:System.Windows.Forms.dll /out:ctrans.exe ctrans.cs

Usage:
  ctrans [lang] [-d]
  ctrans -h, --help, /?  Show this help

Options:
  lang   Target language code (default: ja)
  -d     Output raw API response to stderr

Examples:
  ctrans              Translate to Japanese
  ctrans en           Translate to English
  ctrans zh-CN        Translate to Chinese (Simplified)
  ctrans -d           Translate to Japanese with debug output
  ctrans en -d 2> debug.txt  Save API response to file