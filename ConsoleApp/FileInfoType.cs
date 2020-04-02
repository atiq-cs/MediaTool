﻿using System;
using System.Collections.Generic;
using System.Text;

namespace ConsoleApp {
  /// <summary>
  /// Props/methods related to single file processing
  /// <remarks>
  /// This internal class should not be aware of outer cless details such as
  /// <c> ShouldSimulate </c>
  /// Assertions
  /// https://docs.microsoft.com/en-us/visualstudio/debugger/assertions-in-managed-code
  /// </remarks>  
  /// </summary>
  class FileInfoType {
      public string Path { get; set; }
      public string Parent { get; set; }
      public bool IsModified { get; set; }
      public int YearPosition { get; set; }
      public int YearLength { get; set; }
      // public string[] Lines { get; set; }

      public void Init(string Path) {
        this.Path = Path;
        this.Parent = System.IO.Path.GetDirectoryName(Path);
        IsModified = false;
        // Lines = null;
        ModInfo = string.Empty;
      }
      public void SetDirtyFlag(string str) {
        if (IsModified) {
          if (! ModInfo.Contains(str))
            ModInfo += ", " + str;
        }
        else {
          IsModified = true;
          System.Diagnostics.Debug.Assert(string.IsNullOrEmpty(ModInfo));
          ModInfo += str;
        }
      }

      // list of actions being performed in the file
      public string ModInfo { get; set; }
  }
}