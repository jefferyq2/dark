﻿using System;
using System.Runtime.InteropServices;

namespace LibTreeSitter.CSharp.Darklang
{
  public static class DarklangLanguage
  {
    private const string DllName = "tree-sitter-darklang";

    [DllImport(DllName)]
    private static extern IntPtr tree_sitter_darklang();

    public static Main.Language Create() => new Main.Language(tree_sitter_darklang());
  }
}
