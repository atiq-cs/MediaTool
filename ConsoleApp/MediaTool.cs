// Copyright (c) FFTSys Inc. All rights reserved.
// Use of this source code is governed by a GPL-v3 license

namespace ConsoleApp {
  using System;
  using System.IO;
  using System.Threading.Tasks;
  using System.Collections.Generic;
  using SharpCompress.Archives;

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
      ExtractMedia,   // media = mp4 + srt
      CreateArchive
    };

    /// <summary>
    /// Location of input source file or directory to process
    /// Value being empty/null indicates an error state. Most methods should
    /// not be called in such error state
    /// </summary>
    private string MediaLocation { get; set; }

    // States from CLA
    private CONVERTSTAGE Stage { get; set; }
    private bool IsSingleStaged { get; set; }
    private bool ShouldSimulate { get; set; }
    private bool ShouldShowFFV { get; set; }

    // private HashSet<string> ExclusionDirList;
    // private HashSet<string> SupportedExtList;

    // Actions Summary Related
    private int ModifiedFileCount = 0;
    private int FailedFileCount = 0;
    // FileInfo for our media files
    internal FileInfoType mFileInfo = new FileInfoType();

    /// <summary>
    /// Constructor: sets first 5 properties
    /// </summary>
    public MediaTool(string path, bool isSingleStaged, bool shouldSimulate) {
      // Sets path member and directory flag
      this.MediaLocation = path;
      this.IsSingleStaged = isSingleStaged;
      this.ShouldSimulate = shouldSimulate;

      // SupportedExtList = new HashSet<string>() { "mp4", "mkv", "m4v" };
    }

    /// <summary>
    /// Allows alternative instantiation of the class for ffmpeg calls
    /// </summary>
    public MediaTool(bool ShowFFMpegVersion) {
      this.ShouldShowFFV = ShowFFMpegVersion;
    }

    /// <summary>
    /// Extract Rar Archives and clean up
    /// 
    /// https://docs.microsoft.com/en-us/dotnet/api/system.io.directory.getfiles
    /// 
    /// References
    /// base URL https://github.com/adamhathcock/sharpcompress/blob/master
    /// USAGE.md
    /// src/SharpCompress/Common/ExtractionOptions.cs
    /// Deletion of compressed file after extracting it with nunrar
    ///  https://stackoverflow.com/q/17467951
    /// </summary>
    /// <param name="filePath">the file path variable</param>
    /// <returns></returns>
    public bool ExtractRar(string filePath) {
      if (! IsSupportedArchive(filePath))
        return false;

      mFileInfo.Update("extract");

      using (var archive = SharpCompress.Archives.Rar.RarArchive.Open(filePath)) {
        // archive not null when next archive is not found 
        try {
          // ToDO: show warning for multiple files
          foreach (var entry in archive.Entries) {
            // simulation does not continue because file is not extracted
            // extracting file during simulation: is it a good idea?
            mFileInfo.Path = mFileInfo.Parent + "\\" + entry.Key;

            HasEnoughFreeSpace(entry.Size, 2);

            if (!ShouldSimulate && !mFileInfo.IsInError)
              entry.WriteToDirectory(mFileInfo.Parent, new SharpCompress.Common.ExtractionOptions() {
                ExtractFullPath = true,
                Overwrite = true }
              );

            break;    // only get first item, for psa that's what we expect that much
          }
        }
        catch (System.ArgumentException e) {
          Console.WriteLine("Probably could not find next archive! Msg:\r\n" + e.Message);
          mFileInfo.Update("Fail: SharpCompress Rar Open error");
          return false;
        }
      }

      // remove processed Rar Archive/s
      var tail = "part1.rar";

      if (filePath.EndsWith(tail, StringComparison.CurrentCultureIgnoreCase)) {
        // Only get files that begin with the letter "c".
        string sPath = GetSimplifiedPath(filePath);
        var pattern = sPath.Substring(0, sPath.Length - tail.Length) + "part*.rar";
        // Console.WriteLine("Rar find pattern: " + pattern);   debug

        string[] rarFiles = Directory.GetFiles(mFileInfo.Parent, pattern);

        if (!mFileInfo.IsInError)
          foreach (string rarFile in rarFiles) {

          if (!ShouldSimulate)
            FileOperationAPIWrapper.Send(rarFile);
        }
      }
      else if (!ShouldSimulate && !mFileInfo.IsInError) {
        Console.WriteLine("Removing file: " + filePath);
        FileOperationAPIWrapper.Send(filePath);
      }

      return true;
    }

    /// <summary>
    /// Renames media file
    /// <remarks> ExtractRar might have modified file name. Hence, don't pass `filePath` as param,
    /// it should be retrieved from FileInfo </remarks>
    /// ToDo: don't rename if input is mkv or rar
    ///  introduce mFileInfo.OutPath instead
    /// </summary>
    private void RenameFile() {
      string filePath = mFileInfo.Path;

      // don't rename archives
      if (IsSupportedArchive(filePath, false))
        return ;

      var year = GetYear();
      var title = GetTitle();
      var ripInfo = GetRipperInfo();

      var outFileName = mFileInfo.Parent + '\\' + title + ' ' + year + ripInfo;

      if (mFileInfo.Path != outFileName) {
        // May be we need modification flag for each stage i.e., rename, media conversion and so on..
        mFileInfo.Update("rename");
        Console.WriteLine("   " + GetSimplifiedPath(mFileInfo.Path) + ": " + mFileInfo.ModInfo);
        Console.WriteLine("-> " + GetSimplifiedPath(outFileName));
      }

      if (!ShouldSimulate && mFileInfo.IsModified && !mFileInfo.IsInError) {
        // check if file already exists before rename, send to recycle bin if exists :)
        if (File.Exists(outFileName))
          FileOperationAPIWrapper.Send(outFileName);
        File.Move(mFileInfo.Path, outFileName);
        // Update file name so that next stage can pick it up
        mFileInfo.Path = outFileName;
      }
    }



    /// RenameFile Helper Methods Below
    /// <summary>
    /// Considers additional 64 MB
    ///
    /// <remarks> must need simplified path, validator is set to ensure this
    /// Shares `FileInfo.YearPosition` & `YearLength` with GetTitle & GetRipperInfo
    /// </remarks>
    /// </summary>
    private void HasEnoughFreeSpace(long fileSize, int multiplier = 1) {
      long freeSpaceBytes = 0;
      try {
        freeSpaceBytes = new DriveInfo(mFileInfo.Path.Substring(0, 1)).AvailableFreeSpace;
      }
      catch (ArgumentException e) {
        Console.WriteLine("Invalid drive: " + e.Message);
      }

      long requiredSpaceBytes = multiplier * fileSize + 64 * 1024 * 1024;

      if (freeSpaceBytes < requiredSpaceBytes) {
        mFileInfo.Update("Fail: not enough free space in drive "+ mFileInfo.Path.Substring(0, 1));
        Console.WriteLine("Not enough free space in " + mFileInfo.Path.Substring(0, 1) +
          " drive! Required more " + requiredSpaceBytes / (1024*1024) + " MB for the operation.");
      }
    }

    /// RenameFile Helper Methods Below
    /// <summary>
    /// Converts to format ' YYYY'
    /// similar to imdb style rename: ' (YYYY)'; however, drop the parenthesis
    ///
    /// ToDo: should be used only for movies (when TV show check fails)
    ///  call with prefix for movie part coz that guarantees a year to exist
    ///  be lenient on names with non-existing year
    /// 
    /// <remarks> must need simplified path, validator is set to ensure this
    /// Shares `FileInfo.YearPosition` & `YearLength` with GetTitle & GetRipperInfo
    /// </remarks>  
    /// </summary>
    private string GetYear() {
      var fileName = GetSimplifiedPath(mFileInfo.Path);

      // find index of Year of the movie
      // 3 types: 1. .YYYY. 2.  YYYY  3. (YYYY)
      //  can be covered two regular expressions
      // (.)\d\d\d\d(.) & // (.)\(\d\d\d\d\)(.)
      // replace all the dots with space till that index
      var year = "";
      var match = System.Text.RegularExpressions.Regex.Match(fileName, @"(.|\,| )\(\d{4}\)(.|\,| )");

      // Init year info when `match.Success == false` we don't retain a previous state
      mFileInfo.YearPosition = 0;
      mFileInfo.YearLength = 0;

      if (match.Success)
        year = match.Value.Substring(2, match.Value.Length - 4);
      else
        match = System.Text.RegularExpressions.Regex.Match(fileName, @"(.|,| )\d{4}(.|,| )");

      if (! match.Success)
        return "";

      if (string.IsNullOrEmpty(year))
        year = match.Value.Substring(1, match.Value.Length - 2);

      mFileInfo.YearPosition = match.Index;
      mFileInfo.YearLength = match.Length;
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
    private string GetTitle() {
      // expecting at least 3 chars before year
      if (mFileInfo.YearPosition < 3)
        throw new ArgumentException("Wrong year position: " + mFileInfo.YearPosition + "!");

      var fileName = GetSimplifiedPath(mFileInfo.Path);

      // replace all the dots with space till that index
      var title = fileName.Substring(0, mFileInfo.YearPosition).Replace('.', ' ');
      return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(title);
    }

    /// <summary>
    /// Rename using pattern, usually applies on the tail part of the name
    /// Br.10.6.psa = 720p.BrRip.10bit.6ch.psarip
    /// 
    /// It would be probably a good idea to make the rules dynamic: move to a file
    /// 
    /// Analyze ripper; save in file info
    /// Apply replacement pattern based on ripper, HETeam - 'und' and no info
    /// </summary>
    private string GetRipperInfo() {
      // read these mapping from config file
      var fileName = GetSimplifiedPath(mFileInfo.Path);
      var tail = fileName.Substring(mFileInfo.YearPosition + mFileInfo.YearLength);

      // ToDo: recognize TV Shows
      // apply ripper patterns
      // psa
      // simplify may be using a loop
      if (tail.Contains("x265.HEVC-PSA") || tail.Length == 3 || tail.Substring(0, tail.Length-3).EndsWith("6."))
        mFileInfo.Ripper = "psa";
      else if (tail.Contains("x265.rmteam") || tail.Substring(0, tail.Length - 3).EndsWith("RMT."))
        mFileInfo.Ripper = "RMT";
      else if (tail.Contains("x265-HETeam") || tail.Substring(0, tail.Length - 3).EndsWith("HET.") || tail.Substring(0, tail.Length - 3).EndsWith("HET.Ext"))
        mFileInfo.Ripper = "HET";
      else {
        Console.WriteLine("Unknown ripper! Suffix: " + tail + Environment.NewLine);
        mFileInfo.Ripper = "Unknown";
        // ShouldSimulate = true;   need a force flag
      }

      var oldTail = tail;
      // Console.WriteLine("tail: " + tail);   debug

      // default: empty string for Video: 720p, Bluray, x265 and audio: 2CH
      switch (mFileInfo.Ripper) {
      case "psa":
        tail = tail.Replace("INTERNAL.720p.BrRip.2CH.x265.HEVC-PSA.", "");
        tail = tail.Replace("720p.BluRay.2CH.x265.HEVC-PSA.", "");
        tail = tail.Replace("720p.BrRip.2CH.x265.HEVC-PSA.", "");
        tail = tail.Replace("720p.10bit.BluRay.6CH.x265.HEVC-PSA", "10");
        // see if really BrRip is found in original string
        tail = tail.Replace("1080p.BrRip.6CH.x265.HEVC-PSA", "1080");
        tail = tail.Replace("1080p.BluRay.6CH.x265.HEVC-PSA", "1080");
        // webrip psa
        tail = tail.Replace("720p.WEBRip.2CH.x265.HEVC-PSA", "web");
        tail = tail.Replace("720p.10bit.WEBRip.6CH.x265.HEVC-PSA", "web.10");
        break;

      // RMTeam
      case "RMT":
        tail = tail.Replace("remastered.720p.bluray.hevc.x265.rmteam", "RMT");
        tail = tail.Replace("720p.bluray.hevc.x265.rmteam", "RMT");
        // RMTeam 1080p
        tail = tail.Replace("1080p.bluray.dd5.1.hevc.x265.rmteam", "1080.RMT");
        break;

      // HETeam
      case "HET":
        tail = tail.Replace("720p.BluRay.x265-HETeam", "HET");
        tail = tail.Replace("Extended.1080p.BluRay.x265-HETeam", "1080.HET.Ext");
        tail = tail.Replace("1080p.BluRay.x265-HETeam", "1080.HET");
        break;

      default:
        break;
      }

      if (tail == oldTail && oldTail.EndsWith("mkv"))
        Console.WriteLine("Unknown " + mFileInfo.Ripper + " pattern! Suffix: " + tail);

      if (string.IsNullOrEmpty(tail))
        return string.Empty;
      // drop first char, can be non dot
      return '.' + tail;
    }

    /// <summary>
    /// ToDo: update for new design
    /// Set an action based on user choice and perform action to specified file
    /// </summary>
    private async Task ProcessFile(string filePath) {
      if (! System.IO.Path.HasExtension(filePath)) {
        Console.WriteLine("File does not have an extension!");
        mFileInfo.Update("Fail: has no extension");
        return;
      }

      // If verbose
      // Console.WriteLine("Processing File " + filePath + ":");
      mFileInfo.Init(filePath);

      // we don't need this var: IsSingleStaged anymore
      if (!IsSingleStaged)
        foreach (CONVERTSTAGE stage in (CONVERTSTAGE[])Enum.GetValues(typeof(CONVERTSTAGE))) {
          Stage = stage;
          if (mFileInfo.IsInError)
            break;

          switch (Stage) {
            case CONVERTSTAGE.ExtractArchive:
              ExtractRar(filePath);
              break;
            case CONVERTSTAGE.RenameFile:
              RenameFile();
              break;
            case CONVERTSTAGE.ExtractMedia:
              // accept containers containing subrip: currently only mkv
              if (IsSupportedMedia(mFileInfo.Path) && (!ShouldSimulate || !mFileInfo.ModInfo.Contains("extract"))) {
                // extract already checked for it
                if (! mFileInfo.ModInfo.Contains("extract"))
                  HasEnoughFreeSpace((new System.IO.FileInfo(mFileInfo.Path)).Length);
                if (mFileInfo.IsInError)
                  break;

                var extractSub = new FFMpegUtil(ref mFileInfo);
                mFileInfo = await extractSub.Run(ShouldSimulate);
              }
              break;
            case CONVERTSTAGE.CreateArchive:
              break;
            default:
              throw new InvalidOperationException("Invalid argument " + Stage + " to ProcessFile::switch");
          }
        }

      if (mFileInfo.IsInError)
        FailedFileCount++;
      else if (mFileInfo.IsModified)
        ModifiedFileCount++;
    }

    /// <summary>
    /// Original idea: check if directory qualifies to be in exclusion list
    /// Or if it is in extension list to be excluded for being a file
    /// 
    /// Currently return true for any media we might be interested in examining stream info
    /// </summary>
    private bool IsSupportedMedia(string path) {
      string extension = System.IO.Path.GetExtension(path).Substring(1);
      return (new HashSet<string> { "mp4", "mkv", "m4v", "wmv", "3gp", "m4a"}).Contains(extension);
    }


    /// <summary>
    /// FIles with name like movie-partddd.rar will still fail
    /// We only check upto 2 digits
    /// </summary>
    private bool IsSupportedArchive(string path, bool extractPurpose = true) {
      if (extractPurpose) {
        string pattern1 = @"part\d{1}\.rar$";
        string pattern2 = @"part\d{2}\.rar$";

        if (System.Text.RegularExpressions.Regex.IsMatch(path, pattern1, System.Text.RegularExpressions.
          RegexOptions.IgnoreCase) || System.Text.RegularExpressions.Regex.IsMatch(path, pattern2,
          System.Text.RegularExpressions.RegexOptions.IgnoreCase)) {
          return path.EndsWith("part1.rar", StringComparison.CurrentCultureIgnoreCase);
        }
      }
      string extension = System.IO.Path.GetExtension(path).Substring(1);
      return (new HashSet<string>{ "rar", "tar", "zip" }).Contains(extension);
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

      var dirPath = mFileInfo.Parent;
      var sPath = (path.Length > dirPath.Length && path.StartsWith(dirPath)) ? path.
          Substring(dirPath.Length + 1) : path;

      // file path validator, move to Unit Test, this validator also failing nested files/directories
      if (sPath.Contains("\\"))
        throw new ArgumentException("RenameTitle() requires simplified path (no '\\' in path), you" +
          " provided: '" + sPath + "'");

      return sPath;
    }

    /// <summary>
    /// Process provided directory (recurse)
    /// </summary>
    private async Task ProcessDirectory(string dirPath) {
      Console.WriteLine("Processing Directory " + dirPath + ":");

      // Process the list of files found in the directory.
      string[] fileEntries = Directory.GetFiles(dirPath);
      foreach (string fileName in fileEntries)
        await ProcessFile(fileName);

      // Recurse into subdirectories of this directory.
      string[] subdirectoryEntries = Directory.GetDirectories(dirPath);
      foreach (string subdirectory in subdirectoryEntries)
        await ProcessDirectory(subdirectory);
    }

    public void DisplaySummary() {
      if (ShouldSimulate)
        Console.WriteLine("Simulated summary:");
      Console.WriteLine("Failed    files# " + FailedFileCount);
      Console.WriteLine("Processed files# " + ModifiedFileCount);
    }

    /// <summary>
    /// Automaton of the app
    /// 
    /// ref for enumeration,
    ///  https://stackoverflow.com/q/105372
    /// </summary>
    public async Task Run() {
      if (Directory.Exists(MediaLocation))
        await ProcessDirectory(MediaLocation);
      else
        await ProcessFile(MediaLocation);

      DisplaySummary();
    }
  }
}
