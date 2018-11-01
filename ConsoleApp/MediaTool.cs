// Copyright (c) FFTSys Inc. All rights reserved.
// Use of this source code is governed by a GPL-v3 license

namespace ConsoleApp {
  using System;
  using System.IO;
  using System.Collections.Generic;

  /// <summary>
  /// Perform actions on Media Files
  /// 
  /// Documentation (requirements/features/design decisions) at
  /// https://github.com/atiq-cs/MediaTool/wiki/Design-Requirements
  /// </summary>
  class MediaTool {
    /// <summary>
    /// Location of input source file or directory to process
    /// Value being empty/null indicates an error state. Most methods should
    /// not be called in such  error state
    /// </summary>
    private string Path { get; set; }
    private bool IsDirectory { get; set; }

    // States from CLA
    private bool ShouldRename { get; set; }
    private bool ShouldSimulate { get; set; }
    private bool ShouldShowFFV { get; set; }

    // private HashSet<string> ExclusionDirList;
    private HashSet<string> SupportedExtList;

    // Actions Summary Related
    private int ModifiedFileCount = 0;

    /// <summary>
    /// Props/methods related to single file processing
    /// <remarks>
    /// This internal class should not be aware of outer cless details such as
    /// <c> ShouldSimulate </c>
    /// </remarks>  
    /// </summary>
    class FileInfoType {
      public string Path { get; set; }
      public bool IsModified { get; set; }
      public string[] Lines { get; set; }

      public void Init(string Path) {
        this.Path = Path;
        IsModified = false;
        Lines = null;
        ModInfo = string.Empty;
      }
      public void SetDirtyFlag(string str) {
        if (IsModified) { ModInfo += ", " + str; }
        else {
          IsModified = true;
          ModInfo += str;
        }
      }

      // list of actions being performed in the file
      public string ModInfo { get; set; }
    }
    FileInfoType FileInfo = new FileInfoType();

    /// <summary>
    /// Constructor: sets first 5 properties
    /// </summary>
    public MediaTool(string Path, bool ShouldReplaceTabs, bool ShouldSimulate) {
      // Sets path member and directory flag
      IsDirectory = File.Exists(Path) ? false : true;
      this.Path = Path;
      this.ShouldRename = ShouldReplaceTabs;
      this.ShouldSimulate = ShouldSimulate;

      SupportedExtList = new HashSet<string>() { "mp4", "mkv", "m4v" };
    }

    /// <summary>
    /// Allows alternative instantiation of the class for ffmpeg calls
    /// </summary>
    public MediaTool(bool ShowFFMpegVersion) {
      this.ShouldShowFFV = ShowFFMpegVersion;
    }

    /// <summary>
    /// Replace tabs chars with spaces to specified file
    /// <remarks> This method has its own IO; doesn't use FileInfo </remarks>  
    /// </summary>
    /// <c>isBlockCommentStatusToggling</c>. Verifies output values
    /// Right only verifies using our unique starting style and ending style, ensures there's a
    /// comment line in between.
    /// Later, may be verify if found block contains property as well.
    /// </summary>
    /// <param name="start"> start of result comment block</param>
    /// <param name="end"> end of result comment block</param>
    public void RenameDemo() {
      FileInfo.SetDirtyFlag("rename");
    }


    /// <summary>
    /// ToDo: update for new design
    /// Set an action based on user choice and perform action to specified file
    /// </summary>
    private void ProcessFile(string filePath) {
      var ext = new DirectoryInfo(filePath).Extension.Substring(1);
      if (IsSupportedExt(filePath, ext) == false) {
        Console.WriteLine(" [Ignored] " + GetSimplifiedPath(filePath));
        return;
      }
      FileInfo.Init(filePath);
      if (ShouldRename)
        RenameDemo();
      if (FileInfo.IsModified) {
        Console.WriteLine(" " + GetSimplifiedPath(FileInfo.Path) + ": " + FileInfo.ModInfo);
        ModifiedFileCount++;
        // if (!ShouldSimulate)
        //  FileInfo.WriteFile();
      }
    }

    /// <summary>
    /// Check if directory qualifies to be in exclusion list
    /// Or if it is in extension list to be excluded for being a file
    /// </summary>
    private bool IsSupportedExt(string path, string extension = "") {
      return SupportedExtList.Contains(extension);
    }

    /// <summary>
    /// <param name="FileInfo.Path">Path of source file</param>  
    /// <remarks> Currently applies only to files.</remarks>  
    /// <returns> Returns necessary suffix of file path.</returns>  
    /// </summary>
    private string GetSimplifiedPath(string path) {
      return IsDirectory ? (path.StartsWith(Path) ? path.
          Substring(Path.Length + 1) : string.IsNullOrEmpty(path) ? "." :
          path) : path;
    }

    /// <summary>
    /// Process provided directory (recurse), due to recursion the parameter cannot be replaced
    /// with class property
    /// </summary>
    private void ProcessDirectory(string dirPath) {
      if (IsSupportedExt(dirPath)) {
        Console.WriteLine(" [Ignored] " + GetSimplifiedPath(dirPath));
        return;
      }
      // Process the list of files found in the directory.
      string[] fileEntries = Directory.GetFiles(dirPath);
      foreach (string fileName in fileEntries)
        ProcessFile(fileName);

      // Recurse into subdirectories of this directory.
      string[] subdirectoryEntries = Directory.GetDirectories(dirPath);
      foreach (string subdirectory in subdirectoryEntries)
        ProcessDirectory(subdirectory);
    }

    public void DisplaySummary() {
      if (ShouldSimulate)
        Console.WriteLine("Simulated summary:");
      Console.WriteLine("Number of files modified: " + ModifiedFileCount);
      Console.WriteLine("Following source file types covered:");
    }

    /// <summary>
    /// Automaton of the app
    /// </summary>
    public void Run() {
      Console.WriteLine("Processing " + (IsDirectory ? "Directory: " + Path +
        ", File list:" : "File:"));
      if (IsDirectory) {
        ProcessDirectory(Path);
      }
      else
        ProcessFile(Path);
    }
  }
}
