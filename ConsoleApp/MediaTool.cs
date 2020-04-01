// Copyright (c) FFTSys Inc. All rights reserved.
// Use of this source code is governed by a GPL-v3 license

namespace ConsoleApp {
  using System;
  using System.IO;
  using System.Collections.Generic;
  using System.Threading.Tasks;
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
      ExtractSubtitle,   // sub still part of discussion
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
    FileInfoType FileInfo = new FileInfoType();

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

    public bool ExtractRar(string filePath) {
      if (! IsSupportedArchive(filePath))
        return false;

      FileInfo.SetDirtyFlag("extract");

      using (var archive = SharpCompress.Archives.Rar.RarArchive.Open(filePath)) {
        foreach (var entry in archive.Entries) {
          // simulation does not continue because file is not extracted
          // extracting file during simulation: is it a good idea?
          FileInfo.Path = FileInfo.Parent + "\\" + entry.Key;
          if (!ShouldSimulate)
          {
            entry.WriteToDirectory(FileInfo.Parent, new SharpCompress.Common.ExtractionOptions()
            {
              ExtractFullPath = true,
              Overwrite = true
            });
          }
          break;    // only get first item, for psa that's what we expect that much
        }
      }

      return true;
    }

    /// <summary>
    /// Renames media file
    /// <remarks> ExtractRar might have modified file name. Hence, don't pass `filePath` as param,
    /// it should be retrieved from FileInfo </remarks>
    /// </summary>
    private void RenameFile() {
      string filePath = FileInfo.Path;

      // don't rename archives
      if (IsSupportedArchive(filePath))
        return ;

      var year = GetYear();
      var title = GetTitle();
      var ripInfo = GetRipperInfo();

      var outFileName = FileInfo.Parent + '\\' + title + ' ' + year + ripInfo;

      if (FileInfo.Path != outFileName) {
        // May be we need modification flag for each stage i.e., rename, media conversion and so on..
        FileInfo.SetDirtyFlag("rename");
        Console.WriteLine("   " + GetSimplifiedPath(FileInfo.Path) + ": " + FileInfo.ModInfo);
        Console.WriteLine("-> " + GetSimplifiedPath(outFileName));
      }

      if (!ShouldSimulate && FileInfo.IsModified) {
        File.Move(FileInfo.Path, outFileName);
        // Update file name so that next stage can pick it up
        FileInfo.Path = outFileName;
      }
    }

    /// <summary>
    /// Extract subrip caption from given media file
    /// if only ass is found, convert it to srt using ffmpeg
    /// <remarks>  </remarks>
    /// <c>isBlockCommentStatusToggling</c>. example code block inline
    /// Later, may be verify if found block contains property as well.
    /// Don't pass `filePath` as param, it is updated by rename and should be retrieved from FileInfo
    /// https://docs.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.useshellexecute
    /// </summary>
    private async Task ExtractSubRip() {
      string filePath = FileInfo.Path;
      // accept containers containing subrip: currently only mkv
      if (! ContainerSupportsSub(filePath))
        return ;

      // "D:\PFiles_x64\PT\ffmpeg\bin\ffmpeg.exe"
      var ffmpegPath = @"D:\PFiles_x64\PT\ffmpeg";
      // var mediaInfo = FileInfo.Parent + @"\ffmpeg_media_info.log";
      string ffprobeStr = "";
      Console.WriteLine("Processing Media " + filePath + ":");

      using (System.Diagnostics.Process probeProcess = new System.Diagnostics.Process {
        StartInfo = {
          FileName = ffmpegPath + @"\bin\ffprobe.exe",
          Arguments = "-i \""+ FileInfo.Path + "\"",
          UseShellExecute = false,
          RedirectStandardError = true
        },
        EnableRaisingEvents = true }
      ) {
        try {
          probeProcess.Start();
          ffprobeStr = probeProcess.StandardError.ReadToEnd();
          // we are okay with blocking ffprobe since it takes
          probeProcess.WaitForExit();
          //Console.WriteLine("Received output: " + ffprobeStr);

          if (string.IsNullOrEmpty(ffprobeStr)) {
            probeProcess.Dispose();
            throw new InvalidOperationException("Media Info (probe) is empty for provided media.");
          }

          if (probeProcess.ExitCode != 0) {
            Console.WriteLine("Exit code: " + probeProcess.ExitCode + ", please check if it's corrupted file: " + filePath);
            FileInfo.SetDirtyFlag("Fail: corrupted media file");
          }
          Console.WriteLine("Elapsed time : " + Math.Round((probeProcess.ExitTime - probeProcess.
            StartTime).TotalMilliseconds) + " ms");
          // $"Exit time    : {probeProcess.ExitTime}, " +

          probeProcess.Dispose();
        }
        catch (Exception ex) {
          Console.WriteLine($"An error occurred trying to run ffmpeg probe \"{ffmpegPath}\":\n{ex.Message}");
          return;
        }
      }
      // Wait for ffprobe process Exited event, but not more than 120 seconds
      // await Task.WhenAny(eventHandled.Task, Task.Delay(120000));
      if (string.IsNullOrEmpty(ffprobeStr))
        return;

      var sCodecId = ParseMediaInfo(ffprobeStr);

      if (ShouldSimulate || (System.IO.Path.GetExtension(filePath).ToLower() != ".mkv"))
        return;
      // https://docs.microsoft.com/en-us/dotnet/api/system.io.path.getfilenamewithoutextension
      var srtFilePath = FileInfo.Parent + "\\" + System.IO.Path.GetFileNameWithoutExtension(filePath)
        + ".srt";
      TaskCompletionSource<bool> ffmpegEventHandled = new TaskCompletionSource<bool>();

      using (System.Diagnostics.Process ffmpegProcess = new System.Diagnostics.Process {
        StartInfo = {
          FileName = ffmpegPath + @"\bin\ffmpeg.exe",
          Arguments = " -loglevel fatal -i \""+ FileInfo.Path + "\"" + " -codec:s srt -map " +
            sCodecId + " \"" + srtFilePath + "\"",
          UseShellExecute = false
        },
        EnableRaisingEvents = true }
      ) {
        try {
          // Start a process and raise an event when done.
          ffmpegProcess.Exited += (sender, args) => {
            if (ffmpegProcess.ExitCode != 0) {
              Console.WriteLine("Exit code: " + ffmpegProcess.ExitCode + ", ffmpeg is invoked ",
                "incorrectly! Please check input stream. codec id: " + sCodecId);
            }

            Console.WriteLine("Elapsed time : " + Math.Round((ffmpegProcess.ExitTime - ffmpegProcess.
              StartTime).TotalMilliseconds) + " ms");

            ffmpegEventHandled.TrySetResult(true);
            ffmpegProcess.Dispose();
          };

          ffmpegProcess.Start();
          // better to utilize onExit than `WaitForExit`
        }
        catch (Exception ex)
        {
          Console.WriteLine($"An error occurred trying to run ffmpeg scodec copy \"{ffmpegPath}\":\n{ex.Message}");
          return;
        }

        // Wait for ffmpeg process Exited event, but not more than 120 seconds
        await Task.WhenAny(ffmpegEventHandled.Task, Task.Delay(120000));
      }
    }

    private string ParseMediaInfo(string mediaInfoStr) {
      string sCodeId = string.Empty;

      var needle = "Input #0";
      int start = mediaInfoStr.IndexOf(needle);
      if (start == -1) {
        Console.WriteLine("Container info not found, corrupted file?");
        return sCodeId;
      }

      int prevNeedlePos = start + needle.Length;
      needle = "from '";

      if ((start = mediaInfoStr.IndexOf(needle, prevNeedlePos)) == -1) {
        Console.WriteLine("Container info end needle not found!!");
        return sCodeId;
      }
      Console.WriteLine("Container: " + mediaInfoStr.Substring(prevNeedlePos+2, start-prevNeedlePos-4));

      // assuming container info wouldn't be less than 50, usually filled with metadata and chapter info
      prevNeedlePos = start + needle.Length + 50;
      needle = "Stream #0:";
      var endNeedle = "Metadata:";
      var streamEndNeedle = "_STATISTICS_WRITING_DATE";
      Console.WriteLine("Streams found: ");

      while ((start = mediaInfoStr.IndexOf(needle, prevNeedlePos)) != -1) {
        prevNeedlePos = start + needle.Length;

        if ((start = mediaInfoStr.IndexOf(endNeedle, prevNeedlePos + 2)) == -1) {
          Console.WriteLine("Stream info line end needle not found!");
          return sCodeId;
        }

        var streamInfo = mediaInfoStr.Substring(prevNeedlePos - 2, start - prevNeedlePos - 4);
        Console.WriteLine(" " + streamInfo);
        prevNeedlePos = start + endNeedle.Length;

        if (streamInfo.Contains("Subtitle")) {
          if ((start = mediaInfoStr.IndexOf(streamEndNeedle, prevNeedlePos + 2)) == -1)
            Console.WriteLine("Stream info end needle not found!");
          else {
            Console.WriteLine(mediaInfoStr.Substring(prevNeedlePos+2, start-prevNeedlePos-10));
            prevNeedlePos = start + streamEndNeedle.Length;
          }
        }
      }
      // sCodeId = mediaInfoStr.Substring(start, end - start);

      return "0:2";
      //return sCodeId;
    }


    /// RenameFile Helper Methods Below
    /// <summary>
    /// Converts to format ' YYYY'
    /// similar to imdb style rename: ' (YYYY)'; however, drop the parenthesis
    ///
    /// <remarks> must need simplified path, validator is set to ensure this
    /// Shares `FileInfo.YearPosition` & `YearLength` with GetTitle & GetRipperInfo
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

      if (match.Success)
        year = match.Value.Substring(2, match.Value.Length - 4);
      else
        match = System.Text.RegularExpressions.Regex.Match(fileName, @"(.|,| )\d{4}(.|,| )");

      if (! match.Success)
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
    private async Task ProcessFile(string filePath) {
      if (! System.IO.Path.HasExtension(filePath)) {
        Console.WriteLine("File does not have an extension!");
        FileInfo.SetDirtyFlag("Fail: has no extension");
        return;
      }

      // If verbose
      // Console.WriteLine("Processing File " + filePath + ":");
      FileInfo.Init(filePath);

      if (!IsSingleStaged)
        foreach (CONVERTSTAGE stage in (CONVERTSTAGE[])Enum.GetValues(typeof(CONVERTSTAGE))) {
          Stage = stage;

          bool isSuccess = true;
          switch (Stage) {
            case CONVERTSTAGE.ExtractArchive:
              isSuccess = ExtractRar(filePath);
              break;
            case CONVERTSTAGE.RenameFile:
              RenameFile();
              break;
            case CONVERTSTAGE.ExtractSubtitle:
              if (!ShouldSimulate || (isSuccess && !FileInfo.ModInfo.Contains("extract")))
                await ExtractSubRip();
              break;
            case CONVERTSTAGE.CreateArchive:
              break;
            default:
              throw new InvalidOperationException("Invalid argument " + Stage + " to ProcessFile::switch");
          }
        }

      if (FileInfo.IsModified) {
        ModifiedFileCount++;
      }
    }

    /// <summary>
    /// Original idea: check if directory qualifies to be in exclusion list
    /// Or if it is in extension list to be excluded for being a file
    /// 
    /// Currently return true for any media we might be interested in examining stream info
    /// </summary>
    private bool ContainerSupportsSub(string path) {
      string extension = System.IO.Path.GetExtension(path).Substring(1);
      return (new HashSet<string> { "mp4", "mkv", "m4v", "wmv", "3gp", "m4a"}).Contains(extension);
    }

    private bool IsSupportedArchive(string path) {
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

      var dirPath = FileInfo.Parent;
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
      Console.WriteLine("Number of files modified: " + ModifiedFileCount);
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
