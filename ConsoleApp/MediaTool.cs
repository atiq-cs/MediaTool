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
  ///  https://github.com/atiq-cs/MediaTool/wiki/Design-Requirements
  /// 
  /// Examples at,
  ///  https://github.com/atiq-cs/MediaTool/wiki/Command-Line-Arguments-Design
  /// To keep the file uncluttered, debug statements are in
  //    https://paper.dropbox.com/doc/Media-Tool-Debug-Statements--Av~lz37uTd4Np_PcJIHWlv2fAg-oLfBN5rCuojDp1GPHiXPB
  /// </summary>
  class MediaTool {
    public enum CONVERTSTAGE {
      ExtractArchive,
      RenameFile,
      ConvertMedia,   // sub still part of discussion
      CreateArchive
    };

    /// <summary>
    /// Location of input source file or directory to process
    /// Value being empty/null indicates an error state. Most methods should
    /// not be called in such error state
    /// </summary>
    private string Path { get; set; }
    private bool IsDirectory { get; set; }

    // States from CLA
    private CONVERTSTAGE Stage { get; set; }
    private bool IsSingleStaged { get; set; }
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
      public int YearPosition { get; set; }
      public int YearLength { get; set; }
      // public string[] Lines { get; set; }

      public void Init(string Path) {
        this.Path = Path;
        IsModified = false;
        // Lines = null;
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
    public MediaTool(string Path, bool IsSingleStaged, bool shouldSimulate) {
      // Sets path member and directory flag
      IsDirectory = File.Exists(Path) ? false : true;
      this.Path = Path;
      this.IsSingleStaged = IsSingleStaged;
      this.ShouldSimulate = shouldSimulate;

      // support srt for rename as well
      SupportedExtList = new HashSet<string>() { "mp4", "mkv", "m4v", "srt" };
    }

    /// <summary>
    /// Allows alternative instantiation of the class for ffmpeg calls
    /// </summary>
    public MediaTool(bool ShowFFMpegVersion) {
      this.ShouldShowFFV = ShowFFMpegVersion;
    }

    /// <summary>
    /// Renames media file
    /// <remarks>  </remarks>
    /// </summary>
    /// <c>isBlockCommentStatusToggling</c>. Verifies output values
    /// Right only verifies using our unique starting style and ending style, ensures there's a
    /// comment line in between.
    /// Later, may be verify if found block contains property as well.
    /// </summary>
    /// <param name="start"> start of result comment block</param>
    /// <param name="end"> end of result comment block</param>
    public void Rename() {
      var year = GetYear();
      var title = GetTitle();
      var ripInfo = GetRipperInfo();
      var outFileName = System.IO.Path.GetDirectoryName(FileInfo.Path) + '\\' + title + ' ' + year + ripInfo;
      if (FileInfo.Path != outFileName) {
        // May be we need modification flag for each stage i.e., rename, media conversion and so on..
        FileInfo.SetDirtyFlag("rename");
        Console.WriteLine("   " + GetSimplifiedPath(FileInfo.Path) + ": " + FileInfo.ModInfo);
        Console.WriteLine("-> " + GetSimplifiedPath(outFileName));
      }

      if (!ShouldSimulate && FileInfo.IsModified)
        File.Move(FileInfo.Path, outFileName);
    }

    /// <summary>
    /// Converts to format ' (YYYY)'
    /// as part of imdb style rename: ' (YYYY)'
    /// <remarks> must need simplified path, validator is set to ensure this
    /// Shares `FileInfo.YearPosition` & `YearLength` with GetTitlef & GetRipperInfo
    /// </remarks>  
    /// </summary>
    private string GetYear() {
      var fileName = GetSimplifiedPath(FileInfo.Path);

      // find index of Year of the movie
      // 3 types: 1. .YYYY. 2.  YYYY  3. (YYYY)
      //  can be covered two regular expressions
      // (.)\d\d\d\d(.) & // (.)\(\d\d\d\d\)(.)
      // replace all the dots with space till that index
      var year = "";
      var match = System.Text.RegularExpressions.Regex.Match(fileName, @"(.|\,| )\(\d{4}\)(.|\,| )");
      // Init year info when `match.Success == false` we don't retain a previous state
      FileInfo.YearPosition = 0;
      FileInfo.YearLength = 0;
      if (match.Success == false)
        match = System.Text.RegularExpressions.Regex.Match(fileName, @"(.|,| )\d{4}(.|,| )");
      else
        year = match.Value.Substring(2, match.Value.Length - 4);

      if (match.Success == false)
        return "";
      if (string.IsNullOrEmpty(year))
        year = match.Value.Substring(1, match.Value.Length - 2);
      FileInfo.YearPosition = match.Index;
      FileInfo.YearLength = match.Length;
      return year;
    }

    /// <summary>
    /// Renames to format 'The Title (YYYY)'
    /// Hungarian Rename
    /// imdb style rename, applies the style on year too in format ' (YYYY)'
    /// <remarks> must need simplified path, validator is set to ensure this
    /// Shares `FileInfo.YearPosition` with GetRipperInfo
    /// </remarks>  
    /// </summary>
    private string GetTitle()
    {
      if (FileInfo.YearPosition < 3)
        throw new ArgumentException("Wrong year position!");
      var fileName = GetSimplifiedPath(FileInfo.Path);

      // replace all the dots with space till that index
      var title = fileName.Substring(0, FileInfo.YearPosition).Replace('.', ' ');
      title = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(title);
      return title;
    }

    /// <summary>
    /// Rename using pattern, usually applies on the tail part of the name
    /// Br.10.6.psa = 720p.BrRip.10bit.6ch.psarip
    /// 
    /// It would be probably a good idea to make the rules dynamic: move to a file
    /// </summary>
    private string GetRipperInfo()
    {
      var fileName = GetSimplifiedPath(FileInfo.Path);
      var tail = fileName.Substring(FileInfo.YearPosition + FileInfo.YearLength);
      // apply ripper patterns
      // psa
      tail = tail.Replace("720p.BrRip.2CH.x265.HEVC-PSA", "Br.psa");
      tail = tail.Replace("720p.BluRay.2CH.x265.HEVC-PSA", "Br.psa");
      tail = tail.Replace("720p.10bit.BluRay.6CH.x265.HEVC-PSA", "Br.10.6.psa");
      // found 'INTERNAL' with psa with 2019 Movie
      tail = tail.Replace("INTERNAL.720p.BrRip.2CH.x265.HEVC-PSA", "Br.psa");
      // see if really BrRip is found in original string
      tail = tail.Replace("1080p.BrRip.6CH.x265.HEVC-PSA", "1080p.Br.6.psa");
      tail = tail.Replace("1080p.BluRay.6CH.x265.HEVC-PSA", "1080p.Br.6.psa");
      // webrip psa
      tail = tail.Replace("720p.WEBRip.2CH.x265.HEVC-PSA", "web.psa");
      tail = tail.Replace("720p.10bit.WEBRip.6CH.x265.HEVC-PSA", "web.10.6.psa");
      // RMTeam
      tail = tail.Replace("720p.bluray.hevc.x265.rmteam", "Br.RMTeam");
      tail = tail.Replace("remastered.720p.bluray.hevc.x265.rmteam", "Br.RMTeam");
      // RMTeam 1080p
      tail = tail.Replace("1080p.bluray.dd5.1.hevc.x265.rmteam", "1080p.Br.RMTeam");
      if (string.IsNullOrEmpty(tail))
        return "";
      // drop first char, can be non dot
      return '.' + tail;
    }

    /// <summary>
    /// ToDo: update for new design
    /// Set an action based on user choice and perform action to specified file
    /// </summary>
    private void ProcessFile(string filePath) {

      if (string.IsNullOrEmpty(new DirectoryInfo(filePath).Extension) || IsSupportedExt(filePath,
        new DirectoryInfo(filePath).Extension.Substring(1)) == false) {
        return;
      }
      FileInfo.Init(filePath);
      if (!IsSingleStaged)
        foreach (CONVERTSTAGE stage in (CONVERTSTAGE[])Enum.GetValues(typeof(CONVERTSTAGE)))
        {
          Stage = stage;
          switch (Stage)
          {
            case CONVERTSTAGE.ExtractArchive:
              break;
            case CONVERTSTAGE.RenameFile:
              Rename();
              break;
            case CONVERTSTAGE.ConvertMedia:
              break;
            case CONVERTSTAGE.CreateArchive:
              break;
            default:
              throw new InvalidOperationException("Invalid argument " + Stage + " to ProcessFile::switch");
          }
        }

      if (FileInfo.IsModified) {
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
    /// <remarks> Currently applies only to files; shouldn't be called from places like
    ///  `ProcessDirectory`
    /// ** Be aware of `path` and `Path`.
    /// </remarks>  
    /// <returns> Returns necessary suffix of file path.</returns>
    /// </summary>
    private string GetSimplifiedPath(string path) {
      if (string.IsNullOrEmpty(path))
        throw new ArgumentException("GetSimplifiedPath requires non-empty path!");
      var dirPath = Path;
      if (IsDirectory == false)   // validation
        dirPath = (new FileInfo(path)).DirectoryName;
      var sPath = (path.Length > dirPath.Length && path.StartsWith(dirPath)) ? path.
          Substring(dirPath.Length + 1) : path;
      // file path validator, move to Unit Test, this validator also failing nested files/directories
      if (sPath.Contains("\\"))
        throw new ArgumentException("RenameTitle requires simplified path (no '\\' in path), you" +
          " provided: '" + sPath + "'");
      return sPath;
    }

    /// <summary>
    /// Process provided directory (recurse)
    /// </summary>
    private void ProcessDirectory(string dirPath) {
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
    /// 
    /// ref for enumeration,
    ///  https://stackoverflow.com/q/105372
    /// </summary>
    public void Run() {
      Console.WriteLine("Processing " + (IsDirectory ? "Directory: " + Path +
        ", File list:" : "File:"));
      if (IsDirectory)
      {
        ProcessDirectory(Path);
      }
      else
        ProcessFile(Path);
    }
  }
}
